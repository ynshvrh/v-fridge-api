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
    [InlineData("1 морква", "морква", 1.0, "pcs")]
    [InlineData("2 шт великої моркви", "моркви", 2.0, "шт")]
    [InlineData("200г борошна", "борошна", 200.0, "г")]
    [InlineData("1/2 лимона", "лимона", 0.5, null)]
    [InlineData("1-2 зубчики часнику", "часнику", 2.0, "зубчики")]
    [InlineData("дрібка солі", "солі", 1.0, "дрібка")]
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
        if (expectedUnit is not null)
        {
            result.Unit.Should().Be(expectedUnit);
        }
    }

    [Theory]
    // Valid matches
    [InlineData("Морква", "моркви", true)]
    [InlineData("Яйця", "яйце", true)]
    [InlineData("Куряче філе", "курка", true)]
    [InlineData("Молоко 2.5%", "молоко", true)]
    [InlineData("Картопля молода", "картоплі", true)]
    [InlineData("Помідори чері", "томати", true)]
    [InlineData("Вершкове масло", "масло вершкове", true)]
    // Critical FALSE POSITIVES that must NOT match!
    [InlineData("Сир", "Сироп", false)]
    [InlineData("Кленовий сироп", "Сир твердий", false)]
    [InlineData("Морква", "Морозиво", false)]
    [InlineData("Перець", "Персик", false)]
    [InlineData("Борошно", "Борщ", false)]
    [InlineData("Горіхи", "Горох", false)]
    [InlineData("Макарони", "Мак", false)]
    [InlineData("Малина", "Масло", false)]
    [InlineData("Шоколад", "риба", false)]
    public void IsNameMatch_StrictlyMatchesLegitimateProducts_AndPreventsFalsePositives(string source, string target, bool expected)
    {
        var result = IngredientDeductionHelper.IsNameMatch(source, target);
        result.Should().Be(expected);
    }

    [Fact]
    public void IsOptionalSeasoningOrSauce_DifferentiatesMinorSpicesFromBulkIngredients()
    {
        // 1. Minor spices -> Optional
        var salt = IngredientDeductionHelper.Parse("дрібка солі");
        IngredientDeductionHelper.IsOptionalSeasoningOrSauce(salt).Should().BeTrue();

        var blackPepper = IngredientDeductionHelper.Parse("0.5 ч.л. чорного перцю");
        IngredientDeductionHelper.IsOptionalSeasoningOrSauce(blackPepper).Should().BeTrue();

        var oilSpoon = IngredientDeductionHelper.Parse("1 ст.л. олії");
        IngredientDeductionHelper.IsOptionalSeasoningOrSauce(oilSpoon).Should().BeTrue();

        // 2. Bulk ingredients -> NOT optional
        var bulkButter = IngredientDeductionHelper.Parse("200г вершкового масла");
        IngredientDeductionHelper.IsOptionalSeasoningOrSauce(bulkButter).Should().BeFalse();

        var bulkSugar = IngredientDeductionHelper.Parse("150г цукру");
        IngredientDeductionHelper.IsOptionalSeasoningOrSauce(bulkSugar).Should().BeFalse();

        var meat = IngredientDeductionHelper.Parse("500г курячого філе");
        IngredientDeductionHelper.IsOptionalSeasoningOrSauce(meat).Should().BeFalse();
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
