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
DotNetEnv.Env.TraversePath().Load();

// The Drizzle-owned schema uses `timestamp without time zone`. Opt back into the legacy
// Npgsql DateTime behaviour so DateTime.UtcNow can be stored without manual Kind juggling.
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
    options.UseNpgsql(NormalizeNpgsqlConnectionString(connectionString)));

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
            "{\"role\":\"assistant\",\"content\":\"⚠️ Забагато запитів. Спробуйте через хвилину.\"}", ct);
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

// OpenAPI (.NET 10 built-in)
builder.Services.AddOpenApi();

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
    .WithTags("Meta");

app.MapHealthChecks("/health");

app.MapAuthEndpoints();
app.MapProductsEndpoints();
app.MapChatEndpoints();

app.Run();

// Accept both libpq-style URI ("postgresql://user:pass@host/db?sslmode=require")
// and standard ADO.NET key=value form. Npgsql 10 accepts URIs natively but we
// strip non-Npgsql query params (e.g. channel_binding) to avoid parser errors.
static string NormalizeNpgsqlConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return raw;
    }

    var uri = new Uri(raw);
    var userInfo = uri.UserInfo.Split(':', 2);
    var user = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var database = uri.AbsolutePath.TrimStart('/');

    var sslMode = "Require";
    foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var kv = pair.Split('=', 2);
        if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
        {
            sslMode = kv[1] switch
            {
                "require" => "Require",
                "verify-ca" => "VerifyCA",
                "verify-full" => "VerifyFull",
                "disable" => "Disable",
                _ => "Require"
            };
        }
    }

    var port = uri.Port > 0 ? uri.Port : 5432;
    return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};SslMode={sslMode};Pooling=true;";
}
