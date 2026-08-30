using FluentAssertions;
using VFridge.Api.Services;
using Xunit;

namespace VFridge.Api.Tests.Unit;

public class NutritionCalculatorTests
{
    [Theory]
    [InlineData("Куряче філе", 165)]
    [InlineData("куряча грудка", 165)]
    [InlineData("яйця", 143)]
    [InlineData("рис", 130)]
    [InlineData("молоко", 60)]
    [InlineData("морква", 41)]
    [InlineData("персик", 39)]
    public void FindNutrition_FindsFoodInDatabase(string food, double expectedCalories)
    {
        var info = NutritionCalculator.FindNutrition(food);
        info.Should().NotBeNull();
        info!.Calories.Should().Be(expectedCalories);
    }

    [Fact]
    public void FindNutrition_DoesNotMatchSubstrings_LikeSyrupToCheese()
    {
        var syrup = NutritionCalculator.FindNutrition("Кленовий сироп");
        // Maple syrup must NOT match Cheese (which is 350 kcal)
        if (syrup != null)
        {
            syrup.Calories.Should().NotBe(350);
        }
    }

    [Fact]
    public void CalculateNutrition_CalculatesMacrosAccuratelyForDish()
    {
        var ingredients = new[]
        {
            new ParsedIngredient("300г куряче філе", "Куряче філе", 300, "г"),
            new ParsedIngredient("200г рис", "Рис", 200, "г"),
            new ParsedIngredient("1 шт морква", "Морква", 1, "шт")
        };

        var (calories, prot, fat, carbs) = NutritionCalculator.CalculateNutrition(ingredients, portions: 2);

        calories.Should().BeInRange(380, 420);
        prot.Should().BeGreaterThan(40m);
        carbs.Should().BeGreaterThan(25m);
    }
}
