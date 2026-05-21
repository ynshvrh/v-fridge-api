using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VFridge.Api.Auth;
using VFridge.Api.Configuration;
using VFridge.Api.Data;
using VFridge.Api.Endpoints;
using VFridge.Api.Infrastructure;
using VFridge.Api.Services;

// Load .env if present (Dev convenience). Keys use the standard ASP.NET Core
// section:key form via double underscores — e.g. Email__Password, Jwt__Secret.
// Walks parent directories so `dotnet run` from src/VFridge.Api or the repo root both work.
// NoClobber() so process-level env vars (deployment platforms in prod, integration
// tests in CI/local) win over the .env file.
DotNetEnv.Env.TraversePath().NoClobber().Load();

// The schema (see Migrations/000_initial.sql + 001_auth.sql) uses `timestamp without time
// zone`. Opt back into the legacy Npgsql DateTime behaviour so DateTime.UtcNow can be stored
// without manual Kind juggling.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
// Default config sources (appsettings, env-specific, user-secrets in Dev, env vars, cmd line)
// are already wired up by CreateBuilder. Don't re-add them or they'll override later
// sources (e.g. user-secrets) with the empty defaults from appsettings.json.

// Options binding
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection(CorsOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection(GoogleOptions.SectionName));
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection(OpenRouterOptions.SectionName));

// Database
var connectionString =
    builder.Configuration.GetConnectionString("Default")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("Connection string 'Default' (or DATABASE_URL env var) is required.");

builder.Services.AddDbContext<VFridgeDbContext>(options =>
    options.UseNpgsql(NpgsqlConnectionString.Normalize(connectionString)));

// CORS
const string CorsPolicy = "VFridgeFrontend";
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration
        .GetSection(CorsOptions.SectionName)
        .Get<CorsOptions>()
        ?.AllowedOrigins ?? [];

    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<VFridgeDbContext>("database");

// Rate limiting: 5 requests / 60s per user (mirrors the Next.js implementation)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"code\":\"RATE_LIMITED\",\"role\":\"assistant\",\"content\":\"Too many requests. Try again in a minute.\"}", ct);
    };

    options.AddPolicy("chat", httpContext =>
    {
        var key = httpContext.RequestServices.GetRequiredService<ICurrentUser>().UserId?.ToString()
                  ?? httpContext.Connection.RemoteIpAddress?.ToString()
                  ?? "anon";
        return RateLimitPartition.GetSlidingWindowLimiter(
            key,
            _ => new SlidingWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(60),
                SegmentsPerWindow = 6,
                PermitLimit = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

// Current-user accessor + AI service + auth services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddHttpClient<IAiChatService, OpenRouterChatService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<AuthService>();

// Daily 09:00 Europe/Kyiv: expiry digests + anti-spam cleanup of unverified accounts.
builder.Services.AddSingleton<DailyMaintenanceWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DailyMaintenanceWorker>());

// JWT bearer auth (public stateless API — no cookies)
var jwtOpts = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOpts.Issuer,
            ValidAudience = jwtOpts.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jwtOpts.Secret)
                    ? new string('x', 32) // dummy placeholder so the host can boot before secret is configured
                    : jwtOpts.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// OpenAPI (.NET 10 built-in) — tag descriptions + a friendly Info block
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "V-Fridge API";
        document.Info.Version = "v1";
        document.Info.Description =
            "Public stateless REST API for V-Fridge: auth (email + password, Google ID token, refresh-token rotation), " +
            "per-user product inventory, and AI chef chat. Every error response follows the same `{ code, error }` shape; " +
            "`code` is a stable machine-readable identifier (e.g. `EMAIL_NOT_VERIFIED`).";
        document.Tags = new HashSet<Microsoft.OpenApi.OpenApiTag>
        {
            new() { Name = "Auth",     Description = "Signup, login, refresh, email verification, Google ID-token sign-in." },
            new() { Name = "Products", Description = "Per-user fridge inventory CRUD. All routes require a bearer access token." },
            new() { Name = "Chat",     Description = "AI chef chat. Send and clear history; rate-limited to 5 requests / 60 s per user." },
            new() { Name = "Shopping",  Description = "Per-user shopping list. Mark purchased to convert an item into a product in the fridge." },
            new() { Name = "Analytics", Description = "Aggregates on the consumption log: most wasted, fastest consumed, weekly trends." },
            new() { Name = "Meta",      Description = "Service metadata and health." },
        };
        return Task.CompletedTask;
    });
});

builder.Services.AddProblemDetails();

var app = builder.Build();

// Apply additive SQL migrations
await SqlMigrator.ApplyAsync(
    app.Services,
    Path.Combine(app.Environment.ContentRootPath, "Migrations"));

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Routes
app.MapGet("/", () => Results.Ok(new
    {
        name = "V-Fridge API",
        version = "0.1.0",
        docs = "/openapi/v1.json"
    }))
    .WithName("Root")
    .WithSummary("Service metadata")
    .WithTags("Meta");

app.MapHealthChecks("/health")
    .WithName("Health")
    .WithSummary("Liveness + DbContext check")
    .WithTags("Meta");

app.MapAuthEndpoints();
app.MapProductsEndpoints();
app.MapChatEndpoints();
app.MapShoppingEndpoints();
app.MapAnalyticsEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> can boot the app in integration tests.
public partial class Program { }
