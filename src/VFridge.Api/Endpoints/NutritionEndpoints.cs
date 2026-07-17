using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;

namespace VFridge.Api.Endpoints;

public static class NutritionEndpoints
{
    public static IEndpointRouteBuilder MapNutritionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/nutrition").WithTags("Nutrition");

        group.MapGet("/daily", GetDailyAsync)
            .WithName("GetDailyNutrition")
            .WithSummary("Get daily nutrition summary and logs for a specific date")
            .Produces<DailyNutritionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/log", LogFoodAsync)
            .WithName("LogFood")
            .WithSummary("Log a food item eaten")
            .Produces<NutritionLogResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPut("/log/{id:long}", UpdateLogAsync)
            .WithName("UpdateNutritionLog")
            .WithSummary("Update a nutrition log entry to correct inaccuracies")
            .Produces<NutritionLogResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapDelete("/log/{id:long}", DeleteLogAsync)
            .WithName("DeleteNutritionLog")
            .WithSummary("Delete a nutrition log entry")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/targets", SetTargetsAsync)
            .WithName("SetNutritionTargets")
            .WithSummary("Set daily nutrition goals (calories, protein, fat, carbs)")
            .Produces<NutritionTargetsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> GetDailyAsync(
        string? date,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        DateOnly targetDate;
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, out targetDate))
        {
            targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null) return Results.Unauthorized();

        var logs = await db.NutritionLogs
            .AsNoTracking()
            .Where(l => l.UserId == uid && l.Date == targetDate)
            .OrderBy(l => l.LoggedAt)
            .Select(l => new NutritionLogResponse(
                l.Id,
                l.MealType,
                l.FoodName,
                l.Quantity,
                l.Unit,
                l.Calories,
                l.Protein,
                l.Fat,
                l.Carbs,
                l.LoggedAt))
            .ToListAsync(ct);

        var targets = new NutritionTargetsResponse(
            user.DailyCaloriesTarget,
            user.DailyProteinTarget,
            user.DailyFatTarget,
            user.DailyCarbsTarget);

        var totalCalories = logs.Sum(l => l.Calories);
        var totalProtein = logs.Sum(l => l.Protein);
        var totalFat = logs.Sum(l => l.Fat);
        var totalCarbs = logs.Sum(l => l.Carbs);

        var summary = new NutritionSummaryResponse(totalCalories, totalProtein, totalFat, totalCarbs);

        return Results.Ok(new DailyNutritionResponse(targetDate.ToString("yyyy-MM-dd"), targets, summary, logs));
    }

    private static async Task<IResult> LogFoodAsync(
        LogFoodRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        DateOnly date;
        if (!DateOnly.TryParse(req.Date, out date))
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        var entity = new NutritionLog
        {
            UserId = uid,
            Date = date,
            MealType = req.MealType.Trim().ToLowerInvariant(),
            FoodName = req.FoodName.Trim(),
            Quantity = req.Quantity,
            Unit = req.Unit?.Trim(),
            Calories = req.Calories,
            Protein = req.Protein,
            Fat = req.Fat,
            Carbs = req.Carbs,
            LoggedAt = DateTime.UtcNow
        };

        db.NutritionLogs.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/nutrition/log/{entity.Id}", new NutritionLogResponse(
            entity.Id,
            entity.MealType,
            entity.FoodName,
            entity.Quantity,
            entity.Unit,
            entity.Calories,
            entity.Protein,
            entity.Fat,
            entity.Carbs,
            entity.LoggedAt));
    }

    private static async Task<IResult> UpdateLogAsync(
        long id,
        UpdateLogRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var entity = await db.NutritionLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == uid, ct);
        if (entity is null)
        {
            return Results.NotFound(new ApiError("NUTRITION_LOG_NOT_FOUND", "No log entry found with that ID."));
        }

        entity.FoodName = req.FoodName.Trim();
        entity.MealType = req.MealType.Trim().ToLowerInvariant();
        entity.Quantity = req.Quantity;
        entity.Unit = req.Unit?.Trim();
        entity.Calories = req.Calories;
        entity.Protein = req.Protein;
        entity.Fat = req.Fat;
        entity.Carbs = req.Carbs;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new NutritionLogResponse(
            entity.Id,
            entity.MealType,
            entity.FoodName,
            entity.Quantity,
            entity.Unit,
            entity.Calories,
            entity.Protein,
            entity.Fat,
            entity.Carbs,
            entity.LoggedAt));
    }

    private static async Task<IResult> DeleteLogAsync(
        long id,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var entity = await db.NutritionLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == uid, ct);
        if (entity is null)
        {
            return Results.NotFound(new ApiError("NUTRITION_LOG_NOT_FOUND", "No log entry found with that ID."));
        }

        db.NutritionLogs.Remove(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { message = "Log entry deleted successfully." });
    }

    private static async Task<IResult> SetTargetsAsync(
        SetTargetsRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null) return Results.Unauthorized();

        user.DailyCaloriesTarget = req.Calories;
        user.DailyProteinTarget = req.Protein;
        user.DailyFatTarget = req.Fat;
        user.DailyCarbsTarget = req.Carbs;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new NutritionTargetsResponse(
            user.DailyCaloriesTarget,
            user.DailyProteinTarget,
            user.DailyFatTarget,
            user.DailyCarbsTarget));
    }

    private static bool TryValidate(object instance, out Dictionary<string, string[]> errors)
    {
        var context = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(instance, context, results, validateAllProperties: true))
        {
            errors = null!;
            return true;
        }

        errors = results
            .Where(r => r.MemberNames.Any())
            .GroupBy(r => r.MemberNames.First())
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.ErrorMessage ?? "Invalid field value").ToArray());
        return false;
    }
}

// Request and Response Contracts
public sealed record DailyNutritionResponse(
    string Date,
    NutritionTargetsResponse Targets,
    NutritionSummaryResponse Summary,
    IReadOnlyList<NutritionLogResponse> Logs);

public sealed record NutritionTargetsResponse(
    int? Calories,
    decimal? Protein,
    decimal? Fat,
    decimal? Carbs);

public sealed record NutritionSummaryResponse(
    int Calories,
    decimal Protein,
    decimal Fat,
    decimal Carbs);

public sealed record NutritionLogResponse(
    long Id,
    string MealType,
    string FoodName,
    decimal? Quantity,
    string? Unit,
    int Calories,
    decimal Protein,
    decimal Fat,
    decimal Carbs,
    DateTime LoggedAt);

public sealed class LogFoodRequest
{
    [Required]
    public string Date { get; set; } = null!;

    [Required]
    [RegularExpression("^(breakfast|lunch|dinner|snack)$", ErrorMessage = "MealType must be breakfast, lunch, dinner, or snack.")]
    public string MealType { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string FoodName { get; set; } = null!;

    public decimal? Quantity { get; set; }

    [MaxLength(20)]
    public string? Unit { get; set; }

    [Range(0, 10000, ErrorMessage = "Calories must be positive and less than 10000.")]
    public int Calories { get; set; }

    [Range(0, 1000, ErrorMessage = "Protein must be positive and less than 1000.")]
    public decimal Protein { get; set; }

    [Range(0, 1000, ErrorMessage = "Fat must be positive and less than 1000.")]
    public decimal Fat { get; set; }

    [Range(0, 1000, ErrorMessage = "Carbs must be positive and less than 1000.")]
    public decimal Carbs { get; set; }
}

public sealed class UpdateLogRequest
{
    [Required]
    [RegularExpression("^(breakfast|lunch|dinner|snack)$", ErrorMessage = "MealType must be breakfast, lunch, dinner, or snack.")]
    public string MealType { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string FoodName { get; set; } = null!;

    public decimal? Quantity { get; set; }

    [MaxLength(20)]
    public string? Unit { get; set; }

    [Range(0, 10000, ErrorMessage = "Calories must be positive and less than 10000.")]
    public int Calories { get; set; }

    [Range(0, 1000, ErrorMessage = "Protein must be positive and less than 1000.")]
    public decimal Protein { get; set; }

    [Range(0, 1000, ErrorMessage = "Fat must be positive and less than 1000.")]
    public decimal Fat { get; set; }

    [Range(0, 1000, ErrorMessage = "Carbs must be positive and less than 1000.")]
    public decimal Carbs { get; set; }
}

public sealed class SetTargetsRequest
{
    [Range(0, 10000, ErrorMessage = "Calories must be positive and less than 10000.")]
    public int? Calories { get; set; }

    [Range(0, 1000, ErrorMessage = "Protein must be positive and less than 1000.")]
    public decimal? Protein { get; set; }

    [Range(0, 1000, ErrorMessage = "Fat must be positive and less than 1000.")]
    public decimal? Fat { get; set; }

    [Range(0, 1000, ErrorMessage = "Carbs must be positive and less than 1000.")]
    public decimal? Carbs { get; set; }
}
