using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.Nutrition;

public static class NutritionEndpoints
{
    public static IEndpointRouteBuilder MapNutritionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/nutrition").WithTags("Nutrition");

        group.MapGet("/daily", (string? date, INutritionService service, CancellationToken ct) => service.GetDailyAsync(date, ct))
            .WithName("GetDailyNutrition")
            .WithSummary("Get daily nutrition summary and logs for a specific date")
            .Produces<DailyNutritionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/log", (LogFoodRequest req, INutritionService service, CancellationToken ct) => service.LogFoodAsync(req, ct))
            .WithName("LogFood")
            .WithSummary("Log a food item eaten")
            .Produces<NutritionLogResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPut("/log/{id:long}", (long id, UpdateLogRequest req, INutritionService service, CancellationToken ct) => service.UpdateLogAsync(id, req, ct))
            .WithName("UpdateNutritionLog")
            .WithSummary("Update a nutrition log entry to correct inaccuracies")
            .Produces<NutritionLogResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapDelete("/log/{id:long}", (long id, INutritionService service, CancellationToken ct) => service.DeleteLogAsync(id, ct))
            .WithName("DeleteNutritionLog")
            .WithSummary("Delete a nutrition log entry")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/targets", (SetTargetsRequest req, INutritionService service, CancellationToken ct) => service.SetTargetsAsync(req, ct))
            .WithName("SetNutritionTargets")
            .WithSummary("Set daily nutrition goals (calories, protein, fat, carbs)")
            .Produces<NutritionTargetsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        return app;
    }
}
