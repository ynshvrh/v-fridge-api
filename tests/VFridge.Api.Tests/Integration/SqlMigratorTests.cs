using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VFridge.Api.Data;
using VFridge.Api.Infrastructure;
using VFridge.Api.Tests.Integration.Infrastructure;

namespace VFridge.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class SqlMigratorTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly string _migrationsPath;

    public SqlMigratorTests(PostgresFixture pg)
    {
        _pg = pg;
        // Tests run from bin/Debug/net10.0; walk up to the API project root.
        _migrationsPath = LocateMigrations();
    }

    public async Task InitializeAsync()
    {
        // Start every migrator test from a guaranteed-empty schema. Other classes share the
        // fixture and may have left users / products lying around.
        using var scope = BuildScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "DROP SCHEMA public CASCADE; CREATE SCHEMA public;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Apply_CreatesEverySchemaTable()
    {
        using var scope = BuildScope();

        await SqlMigrator.ApplyAsync(scope.ServiceProvider, _migrationsPath);

        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT table_name FROM information_schema.tables WHERE table_schema='public'")
            .ToListAsync();

        tables.Should().Contain(new[]
        {
            "schema_migrations",
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
        });
    }

    [Fact]
    public async Task Apply_IsIdempotent_AndPreservesOrder()
    {
        using var scope = BuildScope();

        // First run lays the schema down.
        await SqlMigrator.ApplyAsync(scope.ServiceProvider, _migrationsPath);

        // Insert a probe row so we can detect any accidental TRUNCATE-on-re-run.
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO users (username, email, password) VALUES ('probe', 'p@x', 'h')");

        // Second run must no-op.
        await SqlMigrator.ApplyAsync(scope.ServiceProvider, _migrationsPath);

        var rows = await db.Database
            .SqlQueryRaw<string>("SELECT username FROM users")
            .ToListAsync();
        rows.Should().ContainSingle().Which.Should().Be("probe");

        // And the schema_migrations table should record every applied file once.
        var applied = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM schema_migrations ORDER BY name")
            .ToListAsync();
        applied.Should().Equal("000_initial.sql", "001_auth.sql", "002_categories.sql", "003_shopping_items.sql", "004_consumption_log.sql", "005_shared_fridges.sql", "006_username_display_name.sql", "007_user_preferred_language.sql", "008_user_cuisine_preference.sql", "009_shopping_items_fridge_id.sql", "010_meal_plans.sql");
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

    private static string LocateMigrations()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "VFridge.Api", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Cannot locate Migrations folder from test bin.");
    }
}
