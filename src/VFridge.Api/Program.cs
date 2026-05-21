using Microsoft.EntityFrameworkCore;
using VFridge.Api.Configuration;
using VFridge.Api.Data;

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
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));

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

// OpenAPI (.NET 10 built-in)
builder.Services.AddOpenApi();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicy);

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
