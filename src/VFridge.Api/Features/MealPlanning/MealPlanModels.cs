using System.ComponentModel.DataAnnotations;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.MealPlanning;

public sealed record RegenerateDayRequest(
    [property: Required] string Day);

public sealed record RegenerateMealRequest(
    [property: Required] string Day,
    [property: Required] string MealType);

public sealed record GetRecipeRequest(
    [property: Required] string Day,
    string? MealType = null);

public sealed record ImportGapsRequest(
    [property: Required] IReadOnlyList<MealPlanGapItem> Items);

public sealed record ImportGapsResponse(
    int Created,
    int Skipped);
