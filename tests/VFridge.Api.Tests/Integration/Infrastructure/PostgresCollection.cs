namespace VFridge.Api.Tests.Integration.Infrastructure;

/// <summary>
/// All integration test classes join this collection so they share one Postgres container
/// (and thus one connection string) for the entire test run. Per-test isolation is handled
/// by <see cref="TestWebApplicationFactory.ResetDatabaseAsync"/>, which truncates every
/// table between methods.
///
/// One container avoids a process-wide race on the <c>ConnectionStrings__Default</c>
/// environment variable that Program.cs reads at host-build time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
