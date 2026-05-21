using Microsoft.EntityFrameworkCore;
using VFridge.Api.Data;

namespace VFridge.Api.Infrastructure;

/// <summary>
/// Tiny additive-migration runner. Picks up every <c>NNN_*.sql</c> file under
/// <c>Migrations/</c>, hashes its filename, and runs it once per database. Tracked
/// in <c>schema_migrations</c>.
/// </summary>
public static class SqlMigrator
{
    public static async Task ApplyAsync(IServiceProvider services, string migrationsPath, CancellationToken ct = default)
    {
        if (!Directory.Exists(migrationsPath)) return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SqlMigrator");

        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS schema_migrations (name TEXT PRIMARY KEY, applied_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP)",
            ct);

        var applied = new HashSet<string>(
            await db.Database
                .SqlQueryRaw<string>("SELECT name FROM schema_migrations")
                .ToListAsync(ct));

        var files = Directory.GetFiles(migrationsPath, "*.sql").OrderBy(f => f, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (applied.Contains(name)) continue;

            logger.LogInformation("Applying migration {Name}", name);
            var sql = await File.ReadAllTextAsync(file, ct);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(sql, ct);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO schema_migrations(name) VALUES ({0})", [name], ct);
            await tx.CommitAsync(ct);
        }
    }
}
