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
public class MealPlanTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public MealPlanTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();
        var token = await BootstrapVerifiedUserAsync("planner", "planner@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Generate_ReturnsMealsAndGapItems()
    {
        var resp = await _client.PostAsync("/meal-plan", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("meals").GetArrayLength().Should().BeGreaterThan(0);
        body.GetProperty("gapItems").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Generate_PassesUserCuisineAndLanguage_To_Planner()
    {
        var patch = await _client.PatchAsync("/auth/me/preferences",
            JsonContent.Create(new { cuisinePreference = "ukrainian", preferredLanguage = "uk" }));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await _client.PostAsync("/meal-plan", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Planner.LastCuisinePreference.Should().Be("ukrainian");
        _factory.Planner.LastLanguage.Should().Be("uk");
    }

    [Fact]
    public async Task Generate_DefaultsCuisineAndLanguage_WhenNotSet()
    {
        var resp = await _client.PostAsync("/meal-plan", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Planner.LastCuisinePreference.Should().Be("any");
        _factory.Planner.LastLanguage.Should().Be("en");
    }

    [Fact]
    public async Task Generate_BadGatewayWhenPlannerNull()
    {
        _factory.Planner.Response = null;
        var resp = await _client.PostAsync("/meal-plan", null);
        resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Get_ReturnsNoContent_WhenNothingGenerated()
    {
        var resp = await _client.GetAsync("/meal-plan");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Generate_PersistsPlan_AndGetReturnsCachedRow()
    {
        var gen = await _client.PostAsync("/meal-plan", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var generated = await gen.Content.ReadFromJsonAsync<JsonElement>();

        var cached = await _client.GetAsync("/meal-plan");
        cached.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await cached.Content.ReadFromJsonAsync<JsonElement>();

        // Same meals + gaps as the generate response, plus a generatedAt
        // timestamp that round-trips through Postgres.
        body.GetProperty("meals").GetArrayLength()
            .Should().Be(generated.GetProperty("meals").GetArrayLength());
        body.GetProperty("gapItems").GetArrayLength()
            .Should().Be(generated.GetProperty("gapItems").GetArrayLength());
        body.GetProperty("generatedAt").GetDateTime()
            .Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Generate_TwiceOnSameFridge_StoresExactlyOneRow()
    {
        await _client.PostAsync("/meal-plan", null);
        await _client.PostAsync("/meal-plan", null);

        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        (await db.MealPlans.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegenerateDay_ReplacesOnlyThatDay_AndKeepsGaps()
    {
        await _client.PostAsync("/meal-plan", null); // Monday: Tomato pasta, Tuesday: Cheese omelette

        var resp = await _client.PostAsJsonAsync("/meal-plan/regenerate-day", new { day = "Monday" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var meals = body.GetProperty("meals").EnumerateArray().ToList();

        var monday = meals.Single(m => m.GetProperty("day").GetString() == "Monday");
        monday.GetProperty("name").GetString().Should().Be("Borscht", "the Monday meal was regenerated");
        monday.GetProperty("steps").GetArrayLength().Should().BeGreaterThan(0, "the new meal carries cooking steps");

        var tuesday = meals.Single(m => m.GetProperty("day").GetString() == "Tuesday");
        tuesday.GetProperty("name").GetString().Should().Be("Cheese omelette", "other days are untouched");

        body.GetProperty("gapItems").GetArrayLength().Should().Be(2, "the gap list is left untouched on a single-day regen");
    }

    [Fact]
    public async Task RegenerateDay_NotFound_WhenNoPlanGeneratedYet()
    {
        var resp = await _client.PostAsJsonAsync("/meal-plan/regenerate-day", new { day = "Monday" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("MEAL_PLAN_NOT_FOUND");
    }

    [Fact]
    public async Task RegenerateDay_PassesDayPrefsAndAvoidNames_ToPlanner()
    {
        await _client.PatchAsync("/auth/me/preferences",
            JsonContent.Create(new { cuisinePreference = "ukrainian", preferredLanguage = "uk" }));
        await _client.PostAsync("/meal-plan", null);

        var resp = await _client.PostAsJsonAsync("/meal-plan/regenerate-day", new { day = "Tuesday" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Planner.LastRegeneratedDay.Should().Be("Tuesday");
        _factory.Planner.LastCuisinePreference.Should().Be("ukrainian");
        _factory.Planner.LastLanguage.Should().Be("uk");
        _factory.Planner.LastAvoidMealNames.Should().Contain(new[] { "Tomato pasta", "Cheese omelette" });
    }

    [Fact]
    public async Task RegenerateDay_ValidationError_ForUnsupportedDay()
    {
        var resp = await _client.PostAsJsonAsync("/meal-plan/regenerate-day", new { day = "Sunday" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").GetProperty("day")[0].GetString().Should().Contain("Monday");
    }

    [Fact]
    public async Task GetRecipe_FillsInSteps_ForThatDay_AndCaches()
    {
        await _client.PostAsync("/meal-plan", null); // light plan, no steps

        var resp = await _client.PostAsJsonAsync("/meal-plan/recipe", new { day = "Monday" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var monday = body.GetProperty("meals").EnumerateArray()
            .Single(m => m.GetProperty("day").GetString() == "Monday");
        monday.GetProperty("steps").GetArrayLength().Should().BeGreaterThan(0, "the recipe was filled in");
        monday.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();

        _factory.Planner.RecipeCallCount.Should().Be(1);
        _factory.Planner.LastRecipeMealName.Should().Be("Tomato pasta");

        // Second call for the same day must NOT hit the planner again — recipe is cached.
        var again = await _client.PostAsJsonAsync("/meal-plan/recipe", new { day = "Monday" });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Planner.RecipeCallCount.Should().Be(1, "the cached recipe is reused, no extra LLM call");
    }

    [Fact]
    public async Task GetRecipe_PassesPreferredLanguage_ToPlanner()
    {
        await _client.PatchAsync("/auth/me/preferences", JsonContent.Create(new { preferredLanguage = "uk" }));
        await _client.PostAsync("/meal-plan", null);

        var resp = await _client.PostAsJsonAsync("/meal-plan/recipe", new { day = "Tuesday" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Planner.LastRecipeLanguage.Should().Be("uk");
    }

    [Fact]
    public async Task GetRecipe_NotFound_WhenNoPlan()
    {
        var resp = await _client.PostAsJsonAsync("/meal-plan/recipe", new { day = "Monday" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("MEAL_PLAN_NOT_FOUND");
    }

    [Fact]
    public async Task GetRecipe_NotFound_WhenDayHasNoMeal()
    {
        await _client.PostAsync("/meal-plan", null); // only Monday + Tuesday in the fake plan

        var resp = await _client.PostAsJsonAsync("/meal-plan/recipe", new { day = "Friday" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("MEAL_NOT_FOUND");
    }

    [Fact]
    public async Task GetRecipe_ValidationError_ForUnsupportedDay()
    {
        var resp = await _client.PostAsJsonAsync("/meal-plan/recipe", new { day = "Sunday" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportGaps_AddsItems_AndDedupesByName()
    {
        // Pre-seed an existing shopping item.
        await _client.PostAsJsonAsync("/shopping", new { name = "pasta", category = "pantry" });

        var resp = await _client.PostAsJsonAsync("/meal-plan/import-gaps", new
        {
            items = new[]
            {
                new { name = "pasta",        quantity = "200", unit = "g",   category = "pantry" },
                new { name = "tomato sauce", quantity = "1",   unit = "jar", category = "sauces" },
                new { name = "PASTA",        quantity = "100", unit = "g",   category = "pantry" }, // dup
                new { name = "",             quantity = (string?)null, unit = (string?)null, category = "other" }, // skip
            }
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("created").GetInt32().Should().Be(1);
        body.GetProperty("skipped").GetInt32().Should().Be(3);

        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        var names = await db.ShoppingItems.Select(i => i.Name).ToListAsync();
        names.Should().BeEquivalentTo(new[] { "pasta", "tomato sauce" });
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
