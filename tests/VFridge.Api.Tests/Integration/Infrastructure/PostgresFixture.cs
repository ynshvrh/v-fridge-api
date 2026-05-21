using Testcontainers.PostgreSql;

namespace VFridge.Api.Tests.Integration.Infrastructure;

/// <summary>
/// Spins up a Postgres 16 container shared by every test class that opts in to it.
/// One container per fixture (per IClassFixture user) — about 3–5 s of warm-up amortised
/// across the class. The fixture exposes the raw Npgsql connection string so the
/// <see cref="TestWebApplicationFactory"/> can hand it to the host.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("vfridge_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString() + ";Pooling=true;";

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
