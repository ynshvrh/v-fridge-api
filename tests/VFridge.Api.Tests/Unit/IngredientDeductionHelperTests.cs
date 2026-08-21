using FluentAssertions;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;
using Xunit;

namespace VFridge.Api.Tests.Unit;

public class IngredientDeductionHelperTests
{
    [Theory]
    [InlineData("500g flour", "flour", 500.0, "g")]
    [InlineData("2 eggs", "eggs", 2.0, null)]
    [InlineData("2.5 pcs apples", "apples", 2.5, "pcs")]
    [InlineData("Milk", "Milk", -1.0, null)]
    public void Parse_ExtractsQuantityAndNameCorrectly(string rawName, string expectedCleanName, double expectedQty, string? expectedUnit)
    {
        var result = IngredientDeductionHelper.Parse(rawName);

        result.CleanName.Should().Be(expectedCleanName);
        if (expectedQty > 0)
        {
            result.Quantity.Should().Be((decimal)expectedQty);
        }
        else
        {
            result.Quantity.Should().BeNull();
        }
        result.Unit.Should().Be(expectedUnit);
    }

    [Fact]
    public void CalculateMissing_SufficientQuantityInFridge_ReturnsCovered()
    {
        var ingredient = IngredientDeductionHelper.Parse("2 eggs");
        var fridge = new List<Product>
        {
            new Product { Name = "Eggs", Quantity = 6, OwnerId = 1, FridgeId = 1 }
        };
        var shopping = new List<ShoppingItem>();

        var (isCovered, missingQty, _) = IngredientDeductionHelper.CalculateMissing(ingredient, fridge, shopping);

        isCovered.Should().BeTrue();
        missingQty.Should().BeNull();
    }

    [Fact]
    public void CalculateMissing_InsufficientQuantityInFridge_ReturnsMissingDiff()
    {
        var ingredient = IngredientDeductionHelper.Parse("500g flour");
        var fridge = new List<Product>
        {
            new Product { Name = "Flour", Quantity = 200, Unit = "g", OwnerId = 1, FridgeId = 1 }
        };
        var shopping = new List<ShoppingItem>();

        var (isCovered, missingQty, unit) = IngredientDeductionHelper.CalculateMissing(ingredient, fridge, shopping);

        isCovered.Should().BeFalse();
        missingQty.Should().Be(300);
        unit.Should().Be("g");
    }

    [Fact]
    public void CalculateMissing_NoQuantitySpecified_ProductPresentInFridge_ReturnsCovered()
    {
        var ingredient = IngredientDeductionHelper.Parse("Молоко");
        var fridge = new List<Product>
        {
            new Product { Name = "Молоко 2.5%", Quantity = 1, Unit = "l", OwnerId = 1, FridgeId = 1 }
        };
        var shopping = new List<ShoppingItem>();

        var (isCovered, missingQty, _) = IngredientDeductionHelper.CalculateMissing(ingredient, fridge, shopping);

        isCovered.Should().BeTrue();
        missingQty.Should().BeNull();
    }

    [Theory]
    [InlineData(500, "g", "kg", 0.5)]
    [InlineData(1.5, "kg", "g", 1500)]
    [InlineData(250, "ml", "l", 0.25)]
    [InlineData(1, "l", "ml", 1000)]
    [InlineData(3, "pcs", "pcs", 3)]
    public void ConvertQuantity_ConvertsUnitsCorrectly(double qty, string fromUnit, string toUnit, double expected)
    {
        var result = IngredientDeductionHelper.ConvertQuantity((decimal)qty, fromUnit, toUnit);
        result.Should().Be((decimal)expected);
    }

    [Fact]
    public void ParseNutrition_ExtractsCaloriesAndMacros()
    {
        var text = "Смачний борщ • КБЖВ на 1 порцію: 280 кКал | Б: 16г | Ж: 9.5г | В: 32г";
        var (cal, prot, fat, carbs) = IngredientDeductionHelper.ParseNutrition(text);

        cal.Should().Be(280);
        prot.Should().Be(16m);
        fat.Should().Be(9.5m);
        carbs.Should().Be(32m);
    }
}
