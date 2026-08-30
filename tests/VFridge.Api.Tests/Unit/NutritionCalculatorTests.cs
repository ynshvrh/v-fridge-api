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
    public void FindNutrition_FindsFoodInDatabase(string food, double expectedCalories)
    {
        var info = NutritionCalculator.FindNutrition(food);
        info.Should().NotBeNull();
        info!.Calories.Should().Be(expectedCalories);
    }

    [Fact]
    public void CalculateNutrition_CalculatesMacrosAccuratelyForDish()
    {
        // Dish for 2 portions:
        // 300g chicken breast (3 * 165 = 495 kcal, 93g P, 10.8g F, 0g C)
        // 200g rice (2 * 130 = 260 kcal, 5.4g P, 0.6g F, 56g C)
        // 100g carrot (1 * 41 = 41 kcal, 0.9g P, 0.2g F, 9.6g C)
        // Total = 796 kcal, 99.3g P, 11.6g F, 65.6g C
        // Per portion (2 portions) = ~398 kcal, ~49.7g P, ~5.8g F, ~32.8g C
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
