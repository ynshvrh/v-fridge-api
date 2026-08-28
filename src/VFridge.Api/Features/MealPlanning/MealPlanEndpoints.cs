using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.MealPlanning;

public static class MealPlanEndpoints
{
    public static IEndpointRouteBuilder MapMealPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/meal-plan").WithTags("MealPlan");

        group.MapGet("/", (IMealPlanService service, CancellationToken ct) => service.GetCachedAsync(ct))
            .WithName("GetCachedMealPlan")
            .WithSummary("Return the most recently generated plan for the active fridge")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (IMealPlanService service, CancellationToken ct) => service.GenerateAsync(ct))
            .RequireRateLimiting("chat")
            .WithName("GenerateMealPlan")
            .WithSummary("Generate a 5-meal weekday plan")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/regenerate-day", (RegenerateDayRequest req, IMealPlanService service, CancellationToken ct) => service.RegenerateDayAsync(req, ct))
            .RequireRateLimiting("chat")
            .WithName("RegenerateMealPlanDay")
            .WithSummary("Regenerate a single weekday's meal")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();

        group.MapPost("/regenerate-meal", (RegenerateMealRequest req, IMealPlanService service, CancellationToken ct) => service.RegenerateMealAsync(req, ct))
            .RequireRateLimiting("chat")
            .WithName("RegenerateMealPlanMeal")
            .WithSummary("Regenerate a single specific meal of the plan")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();

        group.MapPost("/recipe", (GetRecipeRequest req, IMealPlanService service, CancellationToken ct) => service.GetRecipeAsync(req, ct))
            .RequireRateLimiting("chat")
            .WithName("GetMealRecipe")
            .WithSummary("Lazily fetch a single meal's recipe")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();

        group.MapPost("/import-gaps", (ImportGapsRequest req, IMealPlanService service, CancellationToken ct) => service.ImportGapsAsync(req, ct))
            .WithName("ImportMealPlanGaps")
            .WithSummary("Bulk-append the gap items to the shopping list")
            .Produces<ImportGapsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
