using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFridge.Api.Data;
using VFridge.Api.Tests.Integration.Infrastructure;
using Xunit;

namespace VFridge.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class NutritionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _accessToken = "";

    public NutritionTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();
        _accessToken = await BootstrapVerifiedUserAsync("alice", "alice@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task LogFood_WithoutProductId_Works()
    {
        var log = await _client.PostAsJsonAsync("/nutrition/log", new
        {
            date = "2026-07-20",
            mealType = "breakfast",
            foodName = "Apple",
            quantity = 1,
            unit = "pcs",
            calories = 52,
            protein = 0.3,
            fat = 0.2,
            carbs = 14
        });
        log.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await _client.GetFromJsonAsync<JsonElement>("/nutrition/daily?date=2026-07-20");
        list.GetProperty("logs").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task LogFood_WithProductId_DecrementsQuantity()
    {
        // 1. Create a product in the fridge
        var createProduct = await _client.PostAsJsonAsync("/products", new
        {
            name = "Milk",
            quantity = 500,
            unit = "ml",
            category = "dairy"
        });
        createProduct.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await createProduct.Content.ReadFromJsonAsync<JsonElement>();
        var productId = product.GetProperty("id").GetInt32();

        // 2. Log food with that ProductId consuming 200 ml
        var log = await _client.PostAsJsonAsync("/nutrition/log", new
        {
            date = "2026-07-20",
            mealType = "breakfast",
            foodName = "Milk",
            quantity = 200,
            unit = "ml",
            calories = 100,
            protein = 6,
            fat = 3,
            carbs = 9,
            productId = productId
        });
        log.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. Check product quantity in fridge decremented to 300
        var listProducts = await _client.GetFromJsonAsync<JsonElement>("/products");
        listProducts.GetArrayLength().Should().Be(1);
        listProducts[0].GetProperty("quantity").GetDecimal().Should().Be(300);
    }

    [Fact]
    public async Task LogFood_WithProductId_RemovesProductWhenFullyConsumed()
    {
        // 1. Create a product in the fridge
        var createProduct = await _client.PostAsJsonAsync("/products", new
        {
            name = "Banana",
            quantity = 1,
            unit = "pcs",
            category = "fruits"
        });
        createProduct.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await createProduct.Content.ReadFromJsonAsync<JsonElement>();
        var productId = product.GetProperty("id").GetInt32();

        // 2. Log food with that ProductId consuming 1 pcs
        var log = await _client.PostAsJsonAsync("/nutrition/log", new
        {
            date = "2026-07-20",
            mealType = "snack",
            foodName = "Banana",
            quantity = 1,
            unit = "pcs",
            calories = 89,
            protein = 1.1,
            fat = 0.3,
            carbs = 23,
            productId = productId
        });
        log.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. Check product is removed from the fridge
        var listProducts = await _client.GetFromJsonAsync<JsonElement>("/products");
        listProducts.GetArrayLength().Should().Be(0);
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
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var tempClient = _factory.CreateClient();
        tempClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var fr = await tempClient.PostAsJsonAsync("/fridges", new { name = $"{username}'s fridge" });
        fr.EnsureSuccessStatusCode();

        return token;
    }
}
