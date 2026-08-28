using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Features.Nutrition;

public class NutritionService : INutritionService
{
    private readonly VFridgeDbContext _db;
    private readonly ICurrentUser _me;
    private readonly FridgeContext _fridgeContext;

    public NutritionService(VFridgeDbContext db, ICurrentUser me, FridgeContext fridgeContext)
    {
        _db = db;
        _me = me;
        _fridgeContext = fridgeContext;
    }

    public async Task<IResult> GetDailyAsync(string? date, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        DateOnly targetDate;
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, out targetDate))
        {
            targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null) return Results.Unauthorized();

        var logs = await _db.NutritionLogs
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

    public async Task<IResult> LogFoodAsync(LogFoodRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        DateOnly date;
        if (string.IsNullOrWhiteSpace(req.Date) || !DateOnly.TryParse(req.Date, System.Globalization.CultureInfo.InvariantCulture, out date))
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        // If a ProductId is specified, decrement or remove it from fridge inventory
        if (req.ProductId is int pid)
        {
            var resolved = await _fridgeContext.ResolveAsync(ct);
            if (resolved is null)
            {
                return Results.BadRequest(new ApiError("FRIDGE_NOT_FOUND", "No active fridge resolved for inventory tracking."));
            }

            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == pid && p.FridgeId == resolved.Value.FridgeId, ct);
            if (product is not null)
            {
                var consumeQty = req.Quantity ?? 0;
                if (product.Quantity <= consumeQty)
                {
                    _db.Products.Remove(product);
                }
                else
                {
                    product.Quantity -= consumeQty;
                }
            }
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

        _db.NutritionLogs.Add(entity);
        await _db.SaveChangesAsync(ct);

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

    public async Task<IResult> UpdateLogAsync(long id, UpdateLogRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var entity = await _db.NutritionLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == uid, ct);
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

        await _db.SaveChangesAsync(ct);

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

    public async Task<IResult> DeleteLogAsync(long id, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var entity = await _db.NutritionLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == uid, ct);
        if (entity is null)
        {
            return Results.NotFound(new ApiError("NUTRITION_LOG_NOT_FOUND", "No log entry found with that ID."));
        }

        _db.NutritionLogs.Remove(entity);
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new { message = "Log entry deleted successfully." });
    }

    public async Task<IResult> SetTargetsAsync(SetTargetsRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null) return Results.Unauthorized();

        user.DailyCaloriesTarget = req.Calories;
        user.DailyProteinTarget = req.Protein;
        user.DailyFatTarget = req.Fat;
        user.DailyCarbsTarget = req.Carbs;

        await _db.SaveChangesAsync(ct);

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
