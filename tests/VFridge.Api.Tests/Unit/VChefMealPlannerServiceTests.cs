using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VFridge.Api.Contracts;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Unit;

public class VChefMealPlannerServiceTests
{
    private sealed class FakeVChefClient : IVChefClient
    {
        public VChefMealPlanRequest? LastRequest { get; private set; }
        public VChefMealPlanResponse? ResponseToReturn { get; set; }
        public bool ThrowException { get; set; }

        public Task<VChefRecipeResponse?> GenerateRecipeAsync(VChefGenerateRecipeRequest request, CancellationToken ct = default)
        {
            if (ThrowException) throw new HttpRequestException("Network failure");
            return Task.FromResult<VChefRecipeResponse?>(new VChefRecipeResponse(
                Title: "Pancakes",
                Description: "Delicious pancakes",
                PrepTimeMins: 10,
                CookTimeMins: 15,
                Servings: 2,
                Calories: 400,
                ProteinGrams: 12,
                FatGrams: 8,
                CarbsGrams: 60,
                Ingredients: new(),
                Steps: new() { "Mix", "Fry" },
                GeneratedAt: DateTime.UtcNow));
        }

        public Task<VChefChatResponse?> ChatAsync(VChefChatRequest request, CancellationToken ct = default)
        {
            return Task.FromResult<VChefChatResponse?>(null);
        }

        public Task<VChefMealPlanResponse?> GenerateMealPlanAsync(VChefMealPlanRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (ThrowException) throw new HttpRequestException("VChef microservice is down");
            return Task.FromResult(ResponseToReturn);
        }

        public Task PingHealthAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task GenerateAsync_MapsResponseFromVChefCorrectly()
    {
        var fakeClient = new FakeVChefClient
        {
            ResponseToReturn = new VChefMealPlanResponse(
                Meals: new()
                {
                    new VChefMealPlanMeal("Вівсянка", "Monday", "breakfast", new() { "50г пластівців", "1 яблуко" }, "Смачно", Calories: 350, Protein: 12, Fat: 6, Carbs: 55),
                    new VChefMealPlanMeal("Борщ", "Monday", "lunch", new() { "200г буряка", "150г м'яса" }, "Традиційно", Calories: 480, Protein: 28, Fat: 14, Carbs: 45),
                    new VChefMealPlanMeal("Салат", "Monday", "dinner", new() { "2 помідори", "1 огірок" }, "Легко", Calories: 220, Protein: 6, Fat: 10, Carbs: 18)
                },
                GapItems: new()
                {
                    new VChefMealPlanGapItem("буряк", "200", "г", "produce")
                })
        };

        var service = new VChefMealPlannerService(fakeClient, NullLogger<VChefMealPlannerService>.Instance);
        var inv = new List<MealPlanInventoryItem>
        {
            new("яйця", 10, "шт", "dairy"),
            new("вівсянка", 500, "г", "pantry")
        };

        var plan = await service.GenerateAsync(inv, "ukrainian", "uk", "standard", "Monday", null, CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Meals.Should().HaveCount(3);
        plan.Meals[0].Name.Should().Be("Вівсянка");
        plan.Meals[0].MealType.Should().Be("breakfast");
        plan.GapItems.Should().ContainSingle().Which.Name.Should().Be("буряк");
        fakeClient.LastRequest.Should().NotBeNull();
        fakeClient.LastRequest!.Language.Should().Be("uk");
        fakeClient.LastRequest.Day.Should().Be("Monday");
    }

    [Fact]
    public async Task GenerateAsync_WhenVChefFails_ReturnsNullGracefullyWithoutThrowing()
    {
        var fakeClient = new FakeVChefClient
        {
            ThrowException = true
        };

        var service = new VChefMealPlannerService(fakeClient, NullLogger<VChefMealPlannerService>.Instance);
        var plan = await service.GenerateAsync(new List<MealPlanInventoryItem>(), "ukrainian", "uk", null, "Monday", null, CancellationToken.None);

        plan.Should().BeNull();
    }

    [Fact]
    public async Task GenerateRecipeAsync_WhenVChefSucceeds_MapsRecipeCorrectly()
    {
        var fakeClient = new FakeVChefClient();
        var service = new VChefMealPlannerService(fakeClient, NullLogger<VChefMealPlannerService>.Instance);

        var recipe = await service.GenerateRecipeAsync("Pancakes", new List<string> { "flour", "milk", "eggs" }, "en", CancellationToken.None);

        recipe.Should().NotBeNull();
        recipe!.Description.Should().Be("Delicious pancakes");
        recipe.Steps.Should().HaveCount(2);
        recipe.Calories.Should().Be(400);
    }
}
