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
public class FullDaySimulationTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public FullDaySimulationTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task AntigravityQA_FullDayJourney_EndToEndSimulation()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // =========================================================================
        // STEP 1: Registration, Verification & Profile Preferences
        // =========================================================================
        var token = await BootstrapVerifiedUserAsync("AntigravityQA", "antigravity.qa@vfridge.internal", "AntigravityQA2026!Secure");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Update preferences: Ukrainian cuisine, Ukrainian language, High-protein
        var prefsResp = await _client.PatchAsync("/auth/me/preferences", JsonContent.Create(new
        {
            preferredLanguage = "uk",
            cuisinePreference = "ukrainian",
            dietaryProfile = "high-protein"
        }));
        prefsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify active fridge was provisioned
        var fridgesResp = await _client.GetFromJsonAsync<JsonElement>("/fridges");
        fridgesResp.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        // =========================================================================
        // STEP 2: Stocking the Fridge with Fresh Supermarket Groceries
        // =========================================================================
        var testGroceries = new[]
        {
            new { name = "Куряче філе", quantity = 600m, unit = "г", category = "meat-fish", expiryDays = 2 },
            new { name = "Яйця курячі", quantity = 10m, unit = "шт", category = "dairy", expiryDays = 14 },
            new { name = "Кисломолочний сир 5%", quantity = 350m, unit = "г", category = "dairy", expiryDays = 3 },
            new { name = "Твердий сир", quantity = 200m, unit = "г", category = "dairy", expiryDays = 10 },
            new { name = "Картопля", quantity = 1.5m, unit = "кг", category = "vegetables", expiryDays = 20 },
            new { name = "Помідори чері", quantity = 250m, unit = "г", category = "vegetables", expiryDays = 5 },
            new { name = "Свіжий шпинат", quantity = 100m, unit = "г", category = "vegetables", expiryDays = 2 },
            new { name = "Рис басматі", quantity = 800m, unit = "г", category = "pantry", expiryDays = 180 },
            new { name = "Вівсяні пластівці", quantity = 500m, unit = "г", category = "pantry", expiryDays = 90 },
            new { name = "Банани", quantity = 3m, unit = "шт", category = "fruits", expiryDays = 3 }
        };

        foreach (var item in testGroceries)
        {
            var addResp = await _client.PostAsJsonAsync("/products", new
            {
                name = item.name,
                quantity = item.quantity,
                unit = item.unit,
                category = item.category,
                expirationDate = DateTime.UtcNow.AddDays(item.expiryDays).ToString("yyyy-MM-dd")
            });
            addResp.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Verify products in inventory
        var inventoryResp = await _client.GetFromJsonAsync<JsonElement>("/products");
        inventoryResp.GetArrayLength().Should().Be(10);

        // =========================================================================
        // STEP 3: Morning Breakfast (09:00) — AI Chef Consultation & Calorie Logging
        // =========================================================================
        _factory.Ai.Reply =
            "```recipe\n" +
            "Title: Білковий омлет зі шпинатом та чері\n" +
            "Description: Смачний і швидкий сніданок за 8 хвилин з високим вмістом білка.\n" +
            "Ingredients:\n- 3 шт яйця\n- 50 г шпинат\n- 40 г твердий сир\n- 4 шт помідори чері\n" +
            "Steps:\n1. Збийте яйця з дрібкою солі.\n2. Припустіть шпинат на пательні 1 хв.\n3. Залийте яйцями, викладіть чері та посипте сиром.\n```";

        var chatBreakfastResp = await _client.PostAsJsonAsync("/chat", new
        {
            content = "Що швидкого та білкового приготувати на сніданок з моїх продуктів?"
        });
        chatBreakfastResp.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Ai.LastCuisinePreference.Should().Be("ukrainian");
        _factory.Ai.LastLanguage.Should().Be("uk");

        // Log Breakfast in Nutrition Tracker
        var logBreakfastResp = await _client.PostAsJsonAsync("/nutrition/log", new
        {
            date = today,
            mealType = "breakfast",
            foodName = "Білковий омлет зі шпинатом та чері",
            quantity = 1,
            unit = "порція",
            calories = 330,
            protein = 25m,
            fat = 20m,
            carbs = 7m
        });
        logBreakfastResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // =========================================================================
        // STEP 4: Afternoon Lunch (13:30) — High-Protein Chicken Lunch & Save Recipe
        // =========================================================================
        _factory.Ai.Reply =
            "```recipe\n" +
            "Title: Соковите куряче філе з рисом басматі\n" +
            "Description: Збалансований поживний обід, що утилізує свіже куряче філе.\n" +
            "Ingredients:\n- 250 г куряче філе\n- 70 г рис басматі\n- 50 г помідори чері\n" +
            "Steps:\n1. Відваріть рис басматі у підсоленій воді 12 хв.\n2. Наріжте куряче філе смужками та обсмажте на пательні 7-8 хв.\n3. Подавайте зі свіжими чері.\n```";

        var chatLunchResp = await _client.PostAsJsonAsync("/chat", new
        {
            content = "Потрібен ситний обід з курячим філе, щоб використати його свіжим"
        });
        chatLunchResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Save recipe to Cookbook
        var saveRecipeResp = await _client.PostAsJsonAsync("/saved-recipes", new
        {
            name = "Соковите куряче філе з рисом басматі",
            description = "Збалансований поживний обід, що утилізує свіже куряче філе.",
            ingredients = new[] { "250 г куряче філе", "70 г рис басматі", "50 г помідори чері" },
            steps = new[] { "1. Відваріть рис басматі", "2. Обсмажте куряче філе", "3. Подавайте" },
            calories = 490,
            protein = 48m,
            fat = 9m,
            carbs = 52m
        });
        saveRecipeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Log Lunch in Nutrition Tracker
        var logLunchResp = await _client.PostAsJsonAsync("/nutrition/log", new
        {
            date = today,
            mealType = "lunch",
            foodName = "Соковите куряче філе з рисом басматі",
            quantity = 1,
            unit = "порція",
            calories = 490,
            protein = 48m,
            fat = 9m,
            carbs = 52m
        });
        logLunchResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // =========================================================================
        // STEP 5: Evening Dinner (19:00) — Syrniki / Cottage Cheese
        // =========================================================================
        var logDinnerResp = await _client.PostAsJsonAsync("/nutrition/log", new
        {
            date = today,
            mealType = "dinner",
            foodName = "Ніжні сирники з бананом",
            quantity = 1,
            unit = "порція",
            calories = 360,
            protein = 30m,
            fat = 10m,
            carbs = 38m
        });
        logDinnerResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // =========================================================================
        // STEP 6: Evening Daily Summary & Progress Check (21:00)
        // =========================================================================
        var dailySummary = await _client.GetFromJsonAsync<JsonElement>($"/nutrition/daily?date={today}");
        
        var logs = dailySummary.GetProperty("logs");
        logs.GetArrayLength().Should().Be(3); // Breakfast, Lunch, Dinner

        var summary = dailySummary.GetProperty("summary");
        var totalCalories = summary.GetProperty("calories").GetInt32();
        var totalProtein = summary.GetProperty("protein").GetDecimal();
        var totalFat = summary.GetProperty("fat").GetDecimal();
        var totalCarbs = summary.GetProperty("carbs").GetDecimal();

        // 330 + 490 + 360 = 1180 kcal
        totalCalories.Should().Be(1180);
        // 25 + 48 + 30 = 103g protein
        totalProtein.Should().Be(103m);
        // 20 + 9 + 10 = 39g fat
        totalFat.Should().Be(39m);
        // 7 + 52 + 38 = 97g carbs
        totalCarbs.Should().Be(97m);

        // Verify Saved Recipes list
        var savedList = await _client.GetFromJsonAsync<JsonElement>("/saved-recipes");
        savedList.GetArrayLength().Should().Be(1);
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
