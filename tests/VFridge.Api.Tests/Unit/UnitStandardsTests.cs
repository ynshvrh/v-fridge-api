using FluentAssertions;
using VFridge.Api.Contracts;
using Xunit;

namespace VFridge.Api.Tests.Unit;

public class UnitStandardsTests
{
    [Theory]
    [InlineData("кг", UnitStandards.Kilogram)]
    [InlineData("kg", UnitStandards.Kilogram)]
    [InlineData("кілограм", UnitStandards.Kilogram)]
    [InlineData("г", UnitStandards.Gram)]
    [InlineData("g", UnitStandards.Gram)]
    [InlineData("грам", UnitStandards.Gram)]
    [InlineData("мл", UnitStandards.Milliliter)]
    [InlineData("ml", UnitStandards.Milliliter)]
    [InlineData("л", UnitStandards.Liter)]
    [InlineData("l", UnitStandards.Liter)]
    [InlineData("шт", UnitStandards.Piece)]
    [InlineData("pcs", UnitStandards.Piece)]
    [InlineData("штук", UnitStandards.Piece)]
    [InlineData("ст. л.", UnitStandards.Tablespoon)]
    [InlineData("tbsp", UnitStandards.Tablespoon)]
    [InlineData("ч. л.", UnitStandards.Teaspoon)]
    [InlineData("tsp", UnitStandards.Teaspoon)]
    [InlineData("дрібка", UnitStandards.Pinch)]
    [InlineData("pinch", UnitStandards.Pinch)]
    [InlineData("зубчик", UnitStandards.Clove)]
    [InlineData("clove", UnitStandards.Clove)]
    [InlineData("порцій", UnitStandards.Servings)]
    [InlineData("servings", UnitStandards.Servings)]
    [InlineData("уп", UnitStandards.Pack)]
    [InlineData("pack", UnitStandards.Pack)]
    public void Normalize_MapsSynonymsToCanonicalKey(string raw, string expectedCanonical)
    {
        UnitStandards.Normalize(raw).Should().Be(expectedCanonical);
    }

    [Theory]
    [InlineData(UnitStandards.Piece, "uk", "шт")]
    [InlineData(UnitStandards.Piece, "en", "pcs")]
    [InlineData("шт", "en", "pcs")]
    [InlineData(UnitStandards.Gram, "uk", "г")]
    [InlineData(UnitStandards.Gram, "en", "g")]
    [InlineData(UnitStandards.Kilogram, "uk", "кг")]
    [InlineData(UnitStandards.Kilogram, "en", "kg")]
    [InlineData(UnitStandards.Milliliter, "uk", "мл")]
    [InlineData(UnitStandards.Milliliter, "en", "ml")]
    [InlineData(UnitStandards.Liter, "uk", "л")]
    [InlineData(UnitStandards.Liter, "en", "l")]
    [InlineData(UnitStandards.Tablespoon, "uk", "ст. л.")]
    [InlineData(UnitStandards.Tablespoon, "en", "tbsp")]
    [InlineData(UnitStandards.Teaspoon, "uk", "ч. л.")]
    [InlineData(UnitStandards.Teaspoon, "en", "tsp")]
    [InlineData(UnitStandards.Pinch, "uk", "дрібка")]
    [InlineData(UnitStandards.Pinch, "en", "pinch")]
    [InlineData(UnitStandards.Clove, "uk", "зубчик")]
    [InlineData(UnitStandards.Clove, "en", "clove")]
    [InlineData(UnitStandards.Servings, "uk", "порцій")]
    [InlineData(UnitStandards.Servings, "en", "servings")]
    [InlineData(UnitStandards.Pack, "uk", "уп")]
    [InlineData(UnitStandards.Pack, "en", "pack")]
    public void ToDisplayUnit_TranslatesCorrectly(string unit, string language, string expectedDisplay)
    {
        UnitStandards.ToDisplayUnit(unit, language).Should().Be(expectedDisplay);
    }

    [Fact]
    public void Convert_WeightAndVolume_CalculatesCorrectly()
    {
        UnitStandards.Convert(500, "g", "kg").Should().Be(0.5m);
        UnitStandards.Convert(1.5m, "kg", "g").Should().Be(1500m);
        UnitStandards.Convert(250, "мл", "л").Should().Be(0.25m);
        UnitStandards.Convert(2, "l", "ml").Should().Be(2000m);
        UnitStandards.Convert(3, "шт", "pcs").Should().Be(3m);
    }

    [Fact]
    public void AreCompatible_IdentifiesCompatiblePairs()
    {
        UnitStandards.AreCompatible("г", "кг").Should().BeTrue();
        UnitStandards.AreCompatible("g", "kg").Should().BeTrue();
        UnitStandards.AreCompatible("ml", "l").Should().BeTrue();
        UnitStandards.AreCompatible("шт", "pcs").Should().BeTrue();
        UnitStandards.AreCompatible("г", "ml").Should().BeFalse();
        UnitStandards.AreCompatible("шт", "кг").Should().BeFalse();
    }
}
