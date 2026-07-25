using FluentAssertions;
using VFridge.Api.Contracts;
using VFridge.Api.Services;
using Xunit;

namespace VFridge.Api.Tests.Unit;

public class CategoryInferrerTests
{
    [Theory]
    [InlineData("сіль", ProductCategories.Sauces)]
    [InlineData("олія", ProductCategories.Sauces)]
    [InlineData("оливкова олія", ProductCategories.Sauces)]
    [InlineData("перець чорний", ProductCategories.Sauces)]
    [InlineData("молоко 2.5%", ProductCategories.Dairy)]
    [InlineData("твердий сир", ProductCategories.Dairy)]
    [InlineData("куряче філе", ProductCategories.MeatFish)]
    [InlineData("свинина", ProductCategories.MeatFish)]
    [InlineData("морква", ProductCategories.Vegetables)]
    [InlineData("цибуля", ProductCategories.Vegetables)]
    [InlineData("яблука", ProductCategories.Fruits)]
    [InlineData("борошно", ProductCategories.Pantry)]
    [InlineData("гречка", ProductCategories.Pantry)]
    [InlineData("хліб", ProductCategories.Bakery)]
    [InlineData("вода", ProductCategories.Drinks)]
    [InlineData("вино", ProductCategories.Alcohol)]
    [InlineData("Unknown xyz", ProductCategories.Other)]
    public void InferCategory_CategorizesIngredientsCorrectly(string ingredientName, string expectedCategory)
    {
        var category = CategoryInferrer.InferCategory(ingredientName);
        category.Should().Be(expectedCategory);
    }
}
