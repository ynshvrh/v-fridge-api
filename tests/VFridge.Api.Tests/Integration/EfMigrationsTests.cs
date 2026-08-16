using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VFridge.Api.Data;
using VFridge.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace VFridge.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class EfMigrationsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public EfMigrationsTests(PostgresFixture pg)
    {
        _pg = pg;
    }

    public async Task InitializeAsync()
    {
        // Start every migrator test from a guaranteed-empty schema.
        using var scope = BuildScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MigrateAsync_CreatesEverySchemaTable()
    {
        using var scope = BuildScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();

        await db.Database.MigrateAsync();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT table_name FROM information_schema.tables WHERE table_schema='public'")
            .ToListAsync();

        tables.Should().Contain(new[]
        {
            "__EFMigrationsHistory",
            "users",
            "products",
            "chat",
            "email_verifications",
            "email_verification_tokens",
            "oauth_logins",
            "refresh_tokens",
            "shopping_items",
            "consumption_log",
            "fridges",
            "fridge_members",
            "fridge_invites",
            "nutrition_logs",
            "saved_recipes",
        });
    }

    [Fact]
    public async Task MigrateAsync_IsIdempotent()
    {
        using var scope = BuildScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();

        // First run lays down the schema
        await db.Database.MigrateAsync();

        // Insert a probe row
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO users (username, email, password) VALUES ('probe', 'p@x', 'h')");

        // Second run must no-op
        await db.Database.MigrateAsync();

        var rows = await db.Database
            .SqlQueryRaw<string>("SELECT username FROM users")
            .ToListAsync();
        rows.Should().ContainSingle().Which.Should().Be("probe");

        var applied = await db.Database
            .SqlQueryRaw<string>("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"")
            .ToListAsync();
        applied.Should().Contain(m => m.EndsWith("InitialCreate"));
    }

    private IServiceScope BuildScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddDbContext<VFridgeDbContext>(o =>
        {
            o.UseNpgsql(_pg.ConnectionString);
            o.EnableSensitiveDataLogging();
        });
        return services.BuildServiceProvider().CreateScope();
    }
}
