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
public class ProductsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _accessToken = "";

    public ProductsTests(PostgresFixture pg) => _pg = pg;

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
    public async Task List_WithoutBearer_Returns_401()
    {
        using var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/products");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateAndList_RoundTrip()
    {
        var create = await _client.PostAsJsonAsync("/products", new
        {
            name = "Milk",
            description = "2.5% fat",
            quantity = 1,
            unit = "l",
            expiryDate = "2030-01-01",
            category = "dairy"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await _client.GetFromJsonAsync<JsonElement>("/products");
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("name").GetString().Should().Be("Milk");
        list[0].GetProperty("unit").GetString().Should().Be("l");
        list[0].GetProperty("category").GetString().Should().Be("dairy");
    }

    [Fact]
    public async Task Create_WithoutCategory_DefaultsToOther()
    {
        var create = await _client.PostAsJsonAsync("/products", new
        {
            name = "Mystery item",
            quantity = 1,
            unit = "pcs"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("category").GetString().Should().Be("other");
    }

    [Fact]
    public async Task Create_WithUnknownCategory_FallsBackToOther()
    {
        // The server clamps to the catalog rather than 400-ing — the DB CHECK constraint
        // would reject 'bogus' otherwise.
        var create = await _client.PostAsJsonAsync("/products", new
        {
            name = "Energy bar",
            quantity = 1,
            unit = "pcs",
            category = "bogus"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("category").GetString().Should().Be("other");
    }

    [Fact]
    public async Task Patch_RejectsUnknownCategory()
    {
        var created = await _client.PostAsJsonAsync("/products", new
        {
            name = "Cheese", quantity = 1, unit = "kg", category = "dairy"
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var patch = await _client.PatchAsJsonAsync($"/products/{id}", new { category = "bogus" });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await patch.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").GetProperty("category")[0].GetString().Should().Contain("Unknown");
    }

    [Fact]
    public async Task Create_WithEmptyName_Returns_ValidationError()
    {
        var create = await _client.PostAsJsonAsync("/products", new
        {
            name = "A", // 1 char — under the MinLength(2)
            quantity = 1,
            unit = "pcs"
        });

        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").GetProperty("Name")[0].GetString().Should().Contain("too short");
    }

    [Fact]
    public async Task Delete_OtherOwnersProduct_Returns_404()
    {
        // Create a product as alice.
        var created = await _client.PostAsJsonAsync("/products", new
        {
            name = "Cheese",
            quantity = 1,
            unit = "kg"
        });
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var aliceProductId = createdBody.GetProperty("id").GetInt32();

        // Sign bob in and try to delete alice's row.
        using var bobClient = _factory.CreateClient();
        var bobToken = await BootstrapVerifiedUserAsync("bob", "bob@example.com", "secret456");
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);

        var delete = await bobClient.DeleteAsync($"/products/{aliceProductId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await delete.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PRODUCT_NOT_FOUND");

        // Alice can still see her product.
        var list = await _client.GetFromJsonAsync<JsonElement>("/products");
        list.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Patch_UpdatesOnlyProvidedFields()
    {
        var created = await _client.PostAsJsonAsync("/products", new
        {
            name = "Bread",
            description = "rye",
            quantity = 2,
            unit = "pcs"
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var patch = await _client.PatchAsJsonAsync($"/products/{id}", new { quantity = 5 });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await patch.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("quantity").GetDecimal().Should().Be(5);
        body.GetProperty("name").GetString().Should().Be("Bread"); // unchanged
        body.GetProperty("description").GetString().Should().Be("rye");
    }

    [Fact]
    public async Task CookRecipe_DeductsRawIngredients_AndCreatesPreparedMealContainer()
    {
        // 1. Add raw ingredients
        await _client.PostAsJsonAsync("/products", new { name = "Куряче філе", quantity = 600, unit = "г", category = "meat-fish" });
        await _client.PostAsJsonAsync("/products", new { name = "Рис басматі", quantity = 800, unit = "г", category = "pantry" });

        // 2. Cook a meal
        var cookResp = await _client.PostAsJsonAsync("/products/cook", new
        {
            name = "Запечене куряче філе з рисом",
            portions = 3,
            ingredients = new[] { "300г куряче філе", "200г рис басматі" },
            caloriesPerPortion = 440,
            proteinPerPortion = 38,
            fatPerPortion = 8,
            carbsPerPortion = 52,
            expiryDays = 3
        });

        cookResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookBody = await cookResp.Content.ReadFromJsonAsync<JsonElement>();
        cookBody.GetProperty("deductions").GetArrayLength().Should().Be(2);

        var meal = cookBody.GetProperty("preparedMealProduct");
        meal.GetProperty("name").GetString().Should().Be("Запечене куряче філе з рисом");
        meal.GetProperty("quantity").GetDecimal().Should().Be(3);
        meal.GetProperty("category").GetString().Should().Be("prepared-meals");

        // 3. Verify remaining quantities of raw products in fridge
        var products = await _client.GetFromJsonAsync<JsonElement>("/products");
        products.GetArrayLength().Should().Be(3);

        var chicken = products.EnumerateArray().First(p => p.GetProperty("name").GetString() == "Куряче філе");
        chicken.GetProperty("quantity").GetDecimal().Should().Be(300); // 600 - 300

        var rice = products.EnumerateArray().First(p => p.GetProperty("name").GetString() == "Рис басматі");
        rice.GetProperty("quantity").GetDecimal().Should().Be(600); // 800 - 200
    }

    [Fact]
    public async Task ConsumeProduct_DecrementsPortion_AndLogsToNutritionDiary()
    {
        // 1. Add prepared meal
        var createResp = await _client.PostAsJsonAsync("/products", new
        {
            name = "Український борщ",
            description = "КБЖВ на 1 порцію: 280 кКал | Б: 16г | Ж: 9г | В: 32г",
            quantity = 2,
            unit = "порцій",
            category = "prepared-meals"
        });
        var mealId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // 2. Consume 1 portion
        var eatResp = await _client.PostAsJsonAsync($"/products/{mealId}/consume", new
        {
            portions = 1,
            mealType = "lunch"
        });
        eatResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var eatBody = await eatResp.Content.ReadFromJsonAsync<JsonElement>();
        eatBody.GetProperty("productRemoved").GetBoolean().Should().BeFalse();
        eatBody.GetProperty("remainingQuantity").GetDecimal().Should().Be(1);

        // 3. Verify entry in Nutrition Daily logs
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var nutritionResp = await _client.GetFromJsonAsync<JsonElement>($"/nutrition/daily?date={today}");
        var logs = nutritionResp.GetProperty("logs");
        logs.GetArrayLength().Should().Be(1);
        logs[0].GetProperty("foodName").GetString().Should().Be("Український борщ");
        logs[0].GetProperty("calories").GetInt32().Should().Be(280);
        logs[0].GetProperty("protein").GetDecimal().Should().Be(16);

        // 4. Consume final portion
        var eatFinalResp = await _client.PostAsJsonAsync($"/products/{mealId}/consume", new { portions = 1 });
        eatFinalResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var eatFinalBody = await eatFinalResp.Content.ReadFromJsonAsync<JsonElement>();
        eatFinalBody.GetProperty("productRemoved").GetBoolean().Should().BeTrue();
        eatFinalBody.GetProperty("remainingQuantity").GetDecimal().Should().Be(0);

        // 5. Verify product is gone from fridge
        var products = await _client.GetFromJsonAsync<JsonElement>("/products");
        products.GetArrayLength().Should().Be(0);
    }

    private async Task<string> BootstrapVerifiedUserAsync(string username, string email, string password)
    {
        await _client.PostAsJsonAsync("/auth/signup", new { username, email, password });

        // Mark the email verified directly — the signup tests already exercise the email path.
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
