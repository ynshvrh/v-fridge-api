using System.ComponentModel.DataAnnotations;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.Products;

public sealed record ProductResponse(
    int Id,
    string Name,
    string? Description,
    decimal Quantity,
    string Unit,
    DateOnly? ExpiryDate,
    string Category,
    int OwnerId,
    DateTime? CreatedAt);

public sealed record CreateProductRequest(
    [property: Required, MinLength(2, ErrorMessage = "Name is too short")]
    string Name,
    string? Description,
    [property: Range(0.01, 1_000_000, ErrorMessage = "Quantity must be greater than 0")]
    decimal Quantity,
    [property: Required] string Unit,
    DateOnly? ExpiryDate,
    string? Category);

public sealed record UpdateProductRequest(
    string? Name,
    string? Description,
    decimal? Quantity,
    string? Unit,
    DateOnly? ExpiryDate,
    string? Category);

public sealed record DeductedIngredientSummary(
    string RawIngredient,
    string MatchedProductName,
    decimal DeductedQuantity,
    string Unit,
    bool FullyConsumed);

public sealed record CookRecipeRequest(
    [property: Required, MinLength(2, ErrorMessage = "Recipe name is too short")]
    string Name,
    string? Description = null,
    [property: Range(1, 100, ErrorMessage = "Portions must be between 1 and 100")]
    int Portions = 1,
    IReadOnlyList<string>? Ingredients = null,
    int? CaloriesPerPortion = null,
    decimal? ProteinPerPortion = null,
    decimal? FatPerPortion = null,
    decimal? CarbsPerPortion = null,
    int? ExpiryDays = 3,
    int? SavedRecipeId = null,
    bool IgnoreOptionalMissing = false);

public sealed record CookRecipeResponse(
    ProductResponse PreparedMealProduct,
    IReadOnlyList<DeductedIngredientSummary> Deductions,
    string Message);

public sealed record ConsumeProductRequest(
    [property: Range(0.1, 100, ErrorMessage = "Portions must be at least 0.1")]
    decimal Portions = 1,
    string? MealType = null,
    string? Date = null,
    int? Calories = null,
    decimal? Protein = null,
    decimal? Fat = null,
    decimal? Carbs = null);

public sealed record ConsumeProductResponse(
    bool ProductRemoved,
    decimal RemainingQuantity,
    long? NutritionLogId,
    string Message);
