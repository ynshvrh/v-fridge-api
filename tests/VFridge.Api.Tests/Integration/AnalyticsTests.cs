using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFridge.Api.Data;
using VFridge.Api.Tests.Integration.Infrastructure;

namespace VFridge.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class AnalyticsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public AnalyticsTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();
        var token = await BootstrapVerifiedUserAsync("alex", "alex@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task DeletingExpiredProduct_LogsAs_Expired()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var created = await _client.PostAsJsonAsync("/products", new
        {
            name = "Old yogurt",
            quantity = 1,
            unit = "pcs",
            expiryDate = yesterday.ToString("yyyy-MM-dd"),
            category = "dairy"
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var del = await _client.DeleteAsync($"/products/{id}");
        del.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        var logs = await db.ConsumptionLogs.ToListAsync();
        logs.Should().ContainSingle()
            .Which.Status.Should().Be("expired");
    }

    [Fact]
    public async Task PatchingQuantityToZero_LogsAs_Consumed_AndRemovesRow()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var created = await _client.PostAsJsonAsync("/products", new
        {
            name = "Milk",
            quantity = 1,
            unit = "l",
            expiryDate = tomorrow.ToString("yyyy-MM-dd"),
            category = "dairy"
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var patch = await _client.PatchAsJsonAsync($"/products/{id}", new { quantity = 0 });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await _client.GetFromJsonAsync<JsonElement>("/products");
        list.GetArrayLength().Should().Be(0, "row should be gone once quantity hits 0");

        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        var logs = await db.ConsumptionLogs.ToListAsync();
        logs.Should().ContainSingle()
            .Which.Status.Should().Be("consumed");
    }

    [Fact]
    public async Task Summary_ReturnsAggregates_AfterMixedActivity()
    {
        // Seed a few products and consume / expire them.
        async Task<int> Add(string name, string category, int daysExpiry)
        {
            var d = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysExpiry));
            var resp = await _client.PostAsJsonAsync("/products", new
            {
                name, quantity = 1, unit = "pcs", expiryDate = d.ToString("yyyy-MM-dd"), category
            });
            return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        var milk = await Add("Milk", "dairy", 5);
        var bread = await Add("Bread", "bakery", -1);
        var apples = await Add("Apples", "fruits", -2);

        await _client.PatchAsJsonAsync($"/products/{milk}", new { quantity = 0 });   // consumed
        await _client.DeleteAsync($"/products/{bread}");                             // expired
        await _client.DeleteAsync($"/products/{apples}");                            // expired

        var summary = await _client.GetFromJsonAsync<JsonElement>("/analytics");

        summary.GetProperty("mostWasted").GetArrayLength().Should().BeGreaterThan(0);
        var wastedNames = summary.GetProperty("mostWasted").EnumerateArray()
            .Select(x => x.GetProperty("productName").GetString())
            .ToList();
        wastedNames.Should().Contain("Bread");
        wastedNames.Should().Contain("Apples");

        var fastest = summary.GetProperty("fastestConsumed");
        fastest.GetArrayLength().Should().Be(1);
        fastest[0].GetProperty("productName").GetString().Should().Be("Milk");

        summary.GetProperty("weeklyTrends").GetArrayLength().Should().BeGreaterThan(0);
    }

    private async Task<string> BootstrapVerifiedUserAsync(string username, string email, string password)
    {
        await _client.PostAsJsonAsync("/auth/signup", new { username, email, password });
        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO email_verifications (user_id, verified_at) SELECT id, NOW() FROM users WHERE email = {0} ON CONFLICT DO NOTHING",
                email);
        }
        var login = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
}
