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
public class ShoppingTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public ShoppingTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();
        var token = await BootstrapVerifiedUserAsync("shopper", "shopper@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task List_WithoutBearer_Returns_401()
    {
        using var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/shopping");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateAndList_RoundTrip()
    {
        var resp = await _client.PostAsJsonAsync("/shopping", new
        {
            name = "Bread",
            quantity = 2,
            unit = "pcs",
            category = "bakery"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await _client.GetFromJsonAsync<JsonElement>("/shopping");
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("name").GetString().Should().Be("Bread");
        list[0].GetProperty("category").GetString().Should().Be("bakery");
        list[0].GetProperty("checked").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Patch_TogglesCheckedAndUpdatesFields()
    {
        var created = await _client.PostAsJsonAsync("/shopping", new { name = "Eggs", quantity = 6, unit = "pcs", category = "other" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var patch = await _client.PatchAsJsonAsync($"/shopping/{id}", new { @checked = true, quantity = 12 });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("checked").GetBoolean().Should().BeTrue();
        body.GetProperty("quantity").GetDecimal().Should().Be(12);
        body.GetProperty("name").GetString().Should().Be("Eggs");
    }

    [Fact]
    public async Task Purchase_DeletesShoppingItem_AndCreatesProduct()
    {
        var created = await _client.PostAsJsonAsync("/shopping", new
        {
            name = "Cheddar",
            quantity = 1,
            unit = "kg",
            category = "dairy"
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var purchase = await _client.PostAsJsonAsync($"/shopping/{id}/purchase", new { expiryDate = "2030-01-01" });
        purchase.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await purchase.Content.ReadFromJsonAsync<JsonElement>();
        product.GetProperty("name").GetString().Should().Be("Cheddar");
        product.GetProperty("category").GetString().Should().Be("dairy");
        product.GetProperty("quantity").GetDecimal().Should().Be(1);

        // Shopping item is gone.
        var list = await _client.GetFromJsonAsync<JsonElement>("/shopping");
        list.GetArrayLength().Should().Be(0);

        // Product appears in /products.
        var products = await _client.GetFromJsonAsync<JsonElement>("/products");
        products.GetArrayLength().Should().Be(1);
        products[0].GetProperty("name").GetString().Should().Be("Cheddar");
    }

    [Fact]
    public async Task Patch_UnknownCategory_Returns_ValidationError()
    {
        var created = await _client.PostAsJsonAsync("/shopping", new { name = "Mystery", category = "other" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var patch = await _client.PatchAsJsonAsync($"/shopping/{id}", new { category = "bogus" });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_OtherUsersItem_Returns_404()
    {
        var created = await _client.PostAsJsonAsync("/shopping", new { name = "Mine" });
        var mineId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        using var other = _factory.CreateClient();
        var otherToken = await BootstrapVerifiedUserAsync("other", "other@example.com", "secret123");
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var del = await other.DeleteAsync($"/shopping/{mineId}");
        del.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await del.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("SHOPPING_ITEM_NOT_FOUND");
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
