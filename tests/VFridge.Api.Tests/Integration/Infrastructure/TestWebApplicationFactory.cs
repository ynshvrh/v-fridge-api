using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VFridge.Api.Data;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Integration.Infrastructure;

/// <summary>
/// Boots the real Program.cs host pointed at a Testcontainers Postgres, with the email
/// and AI dependencies replaced by in-memory fakes. Each test class gets a fresh factory
/// + container via <see cref="IClassFixture{PostgresFixture}"/>.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    static TestWebApplicationFactory()
    {
        // Override anything the .env at repo root may have leaked into the test process.
        // These have to land in env vars (not just in-memory config) because Program.cs
        // reads JwtOptions synchronously at host-build time, before our ConfigureAppConfiguration
        // callback gets to add its in-memory source.
        Environment.SetEnvironmentVariable("Jwt__Secret", "test-secret-test-secret-test-secret-32+");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "vfridge-test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "vfridge-test");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "5");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "7");
        Environment.SetEnvironmentVariable("Frontend__BaseUrl", "http://test-spa.local");
        Environment.SetEnvironmentVariable("Email__Host", "smtp.test");
        Environment.SetEnvironmentVariable("Email__Port", "0");
        Environment.SetEnvironmentVariable("Email__User", "noreply@test");
        Environment.SetEnvironmentVariable("Email__Password", "test");
        Environment.SetEnvironmentVariable("Email__From", "noreply@test");
        Environment.SetEnvironmentVariable("OpenRouter__ApiKey", "test-key");
        Environment.SetEnvironmentVariable("OpenRouter__BaseUrl", "https://openrouter.test");
        Environment.SetEnvironmentVariable("OpenRouter__Model", "test-model");
        Environment.SetEnvironmentVariable("Google__ClientId", "");
    }

    public TestWebApplicationFactory(string connectionString)
    {
        ConnectionString = connectionString;
        // ConnectionString varies per test class (one container each), so it has to be set
        // here rather than in the static ctor.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
    }

    public string ConnectionString { get; }

    public FakeEmailSender Emails { get; } = new();

    public FakeAiChatService Ai { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tests run from the test bin/ — point the host at the API project source so
        // SqlMigrator finds Migrations/*.sql and any other content files.
        builder.UseContentRoot(LocateApiProjectRoot());

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Jwt:Secret"] = "test-secret-test-secret-test-secret-32+", // >= 32 chars
                ["Jwt:Issuer"] = "vfridge-test",
                ["Jwt:Audience"] = "vfridge-test",
                ["Jwt:AccessTokenMinutes"] = "5",
                ["Jwt:RefreshTokenDays"] = "7",
                ["Frontend:BaseUrl"] = "http://test-spa.local",
                ["Email:Host"] = "smtp.test",
                ["Email:Port"] = "0",
                ["Email:User"] = "noreply@test",
                ["Email:Password"] = "test",
                ["Email:From"] = "noreply@test",
                ["OpenRouter:ApiKey"] = "test-key", // non-empty so the fallback path is not taken
                ["OpenRouter:BaseUrl"] = "https://openrouter.test",
                ["OpenRouter:Model"] = "test-model",
                ["Google:ClientId"] = "", // disables the Google login route in tests
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace real network dependencies with deterministic fakes.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            services.RemoveAll<IAiChatService>();
            services.AddSingleton<IAiChatService>(Ai);
        });

        builder.UseEnvironment("Development");
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    public async Task ResetDatabaseAsync()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        // Drop and re-apply on each test class. Tables are recreated by the SqlMigrator
        // that runs during the next host start, but the running host's tables stay live
        // for the duration of the class, so we just truncate between methods instead.
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE chat, products, shopping_items, refresh_tokens, email_verification_tokens, email_verifications, oauth_logins, users RESTART IDENTITY CASCADE");
    }

    private static string LocateApiProjectRoot()
    {
        // Walks up from the test bin/ until it finds the API project file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "VFridge.Api", "VFridge.Api.csproj");
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "src", "VFridge.Api");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the VFridge.Api project root from the test bin folder.");
    }
}

