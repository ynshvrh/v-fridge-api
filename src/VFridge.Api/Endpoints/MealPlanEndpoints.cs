using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class MealPlanEndpoints
{
    // JSON shape for the cached blob columns. Kept stable — extending the
    // MealPlanResponse contract is fine, but renaming fields without a
    // migration would silently break restore.
    private static readonly JsonSerializerOptions CacheJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapMealPlanEndpoints(this IEndpointRouteBuilder app)
    {
        // Both GET and POST live in the same group; only POST hits OpenRouter,
        // so we attach the chat rate-limit to it explicitly and leave GET
        // (which is a pure DB read) unthrottled.
        var group = app.MapGroup("/meal-plan").WithTags("MealPlan");

        group.MapGet("/", GetCachedAsync)
            .WithName("GetCachedMealPlan")
            .WithSummary("Return the most recently generated plan for the active fridge")
            .WithDescription("204 No Content when nothing has ever been generated. Clients open the planner screen with this and only POST to regenerate.")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", GenerateAsync)
            .RequireRateLimiting("chat")
            .WithName("GenerateMealPlan")
            .WithSummary("Generate a 5-meal weekday plan")
            .WithDescription("Builds the prompt from the caller's current inventory, generates a fresh plan via the LLM, and upserts it onto the active fridge's cached row (one row per fridge). Reuses the chat rate-limit bucket (5 calls / 60 s per user).")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/regenerate-day", RegenerateDayAsync)
            .RequireRateLimiting("chat")
            .WithName("RegenerateMealPlanDay")
            .WithSummary("Regenerate a single weekday's meal")
            .WithDescription("Replaces one weekday's meal in the active fridge's cached plan with a fresh LLM suggestion, keeping the other days and the gap list untouched. Reuses the chat rate-limit bucket. 404 MEAL_PLAN_NOT_FOUND when no plan has been generated yet.")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();

        group.MapPost("/regenerate-meal", RegenerateMealAsync)
            .RequireRateLimiting("chat")
            .WithName("RegenerateMealPlanMeal")
            .WithSummary("Regenerate a single specific meal of the plan")
            .WithDescription("Replaces a single meal (by day and type) in the active fridge's cached plan with a fresh LLM suggestion. Reuses the chat rate-limit bucket. 404 MEAL_PLAN_NOT_FOUND when no plan exists.")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();

        group.MapPost("/recipe", GetRecipeAsync)
            .RequireRateLimiting("chat")
            .WithName("GetMealRecipe")
            .WithSummary("Lazily fetch a single meal's recipe")
            .WithDescription("Returns the cached plan with the requested day's meal filled in with a description and cooking steps, fetching them from the LLM the first time and caching the result. Keeps each LLM call small enough for the free-tier token budget. 404 MEAL_PLAN_NOT_FOUND when no plan exists; 404 MEAL_NOT_FOUND when that day has no meal.")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();

        group.MapPost("/import-gaps", ImportGapsAsync)
            .WithName("ImportMealPlanGaps")
            .WithSummary("Bulk-append the gap items to the shopping list")
            .WithDescription("Takes a list of meal-plan gap items, dedupes against current unchecked shopping items by name (case-insensitive), and creates rows for the new ones.")
            .Produces<ImportGapsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetCachedAsync(
        VFridgeDbContext db,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var row = await db.MealPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FridgeId == resolved.Value.FridgeId, ct);
        if (row is null) return Results.NoContent();

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(row.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();
        var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(row.GapItemsJson, CacheJson)
                   ?? new List<MealPlanGapItem>();
        var filteredGaps = await FilterGapItemsAsync(db, resolved.Value.FridgeId, meals, gaps, ct);
        return Results.Ok(new MealPlanResponse(meals, filteredGaps, row.UpdatedAt));
    }

    private static bool IsSameCalendarWeek(DateTime date1, DateTime date2)
    {
        return System.Globalization.ISOWeek.GetWeekOfYear(date1) == System.Globalization.ISOWeek.GetWeekOfYear(date2) &&
               System.Globalization.ISOWeek.GetYear(date1) == System.Globalization.ISOWeek.GetYear(date2);
    }

    private static async Task<IResult> GenerateAsync(
        string? currentDay,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        IMealPlannerService planner,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var fridgeId = resolved.Value.FridgeId;
        var inventory = await db.Products
            .Where(p => p.FridgeId == fridgeId)
            .Select(p => new MealPlanInventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
            .ToListAsync(ct);

        var prefs = await db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage, u.DietaryProfile })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        var dietaryProfile = prefs?.DietaryProfile;

        var existing = await db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
        List<MealPlanMeal>? existingMeals = null;
        List<MealPlanGapItem>? existingGaps = null;
        bool isSameWeek = false;

        if (existing is not null)
        {
            isSameWeek = IsSameCalendarWeek(existing.UpdatedAt, DateTime.UtcNow);
            if (isSameWeek)
            {
                existingMeals = JsonSerializer.Deserialize<List<MealPlanMeal>>(existing.MealsJson, CacheJson);
                existingGaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(existing.GapItemsJson, CacheJson);
            }
        }

        string? validatedDay = null;
        if (!string.IsNullOrWhiteSpace(currentDay) && Weekdays.TryGetValue(currentDay.Trim(), out var canonicalDay))
        {
            validatedDay = canonicalDay;
        }

        if (validatedDay is null)
        {
            validatedDay = DateTime.UtcNow.DayOfWeek.ToString();
        }

        var plan = await planner.GenerateAsync(
            inventory,
            cuisinePreference,
            language,
            dietaryProfile,
            currentDay: validatedDay,
            existingMeals: existingMeals,
            ct);

        if (plan is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Merge new gaps with existing gaps if same week
        var candidateGaps = plan.GapItems.ToList();
        if (isSameWeek && validatedDay is not null && existingGaps is not null)
        {
            candidateGaps = existingGaps
                .Concat(plan.GapItems)
                .GroupBy(g => g.Name.ToLower().Trim())
                .Select(grp => grp.First())
                .ToList();
        }

        var filteredGaps = await FilterGapItemsAsync(db, fridgeId, plan.Meals, candidateGaps, ct);

        var now = DateTime.UtcNow;
        var mealsJson = JsonSerializer.Serialize(plan.Meals, CacheJson);
        var gapsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);

        if (existing is null)
        {
            db.MealPlans.Add(new MealPlanRecord
            {
                FridgeId = fridgeId,
                MealsJson = mealsJson,
                GapItemsJson = gapsJson,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.MealsJson = mealsJson;
            existing.GapItemsJson = gapsJson;
            existing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(plan.Meals, filteredGaps, now));
    }

    public sealed record RegenerateDayRequest(string Day);
    public sealed record RegenerateMealRequest(string Day, string MealType);

    // The seven weekdays the planner assigns meals to. Case-insensitive match; the canonical
    // English code is what gets stored and pinned on the regenerated meal.
    private static readonly Dictionary<string, string> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monday"] = "Monday",
        ["tuesday"] = "Tuesday",
        ["wednesday"] = "Wednesday",
        ["thursday"] = "Thursday",
        ["friday"] = "Friday",
        ["saturday"] = "Saturday",
        ["sunday"] = "Sunday",
    };

    private static async Task<IResult> RegenerateDayAsync(
        RegenerateDayRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        IMealPlannerService planner,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        if (req.Day is null || !Weekdays.TryGetValue(req.Day.Trim(), out var day))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["day"] = ["Day must be one of Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday"]
            });
        }

        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();
        var fridgeId = resolved.Value.FridgeId;

        var row = await db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
        if (row is null)
            return Results.NotFound(new { code = "MEAL_PLAN_NOT_FOUND", error = "No meal plan to regenerate. Generate one first." });

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(row.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();
        var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(row.GapItemsJson, CacheJson)
                   ?? new List<MealPlanGapItem>();

        var inventory = await db.Products
            .Where(p => p.FridgeId == fridgeId)
            .Select(p => new MealPlanInventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
            .ToListAsync(ct);

        var prefs = await db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage, u.DietaryProfile })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        var dietaryProfile = prefs?.DietaryProfile;

        // Avoid repeating any dish already in the plan (including the one being replaced).
        var avoid = meals.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        var newMeals = await planner.RegenerateDayAsync(inventory, cuisinePreference, language, day, avoid, dietaryProfile, ct);
        if (newMeals is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Replace the meal(s) for that day; keep everyone else. Per the product decision the gap
        // list is left untouched on a single-day regenerate.
        meals.RemoveAll(m => string.Equals(m.Day, day, StringComparison.OrdinalIgnoreCase));
        meals.AddRange(newMeals);

        var now = DateTime.UtcNow;
        row.MealsJson = JsonSerializer.Serialize(meals, CacheJson);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var filteredGaps = await FilterGapItemsAsync(db, fridgeId, meals, gaps, ct);
        row.GapItemsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(meals, filteredGaps, now));
    }

    private static async Task<IResult> RegenerateMealAsync(
        RegenerateMealRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        IMealPlannerService planner,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        if (req.Day is null || !Weekdays.TryGetValue(req.Day.Trim(), out var day))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["day"] = ["Day must be one of Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday"]
            });
        }

        if (string.IsNullOrWhiteSpace(req.MealType) ||
            (!string.Equals(req.MealType, "breakfast", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(req.MealType, "lunch", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(req.MealType, "dinner", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mealType"] = ["MealType must be one of breakfast, lunch, dinner"]
            });
        }

        var normalizedMealType = req.MealType.Trim().ToLowerInvariant();

        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();
        var fridgeId = resolved.Value.FridgeId;

        var row = await db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
        if (row is null)
            return Results.NotFound(new { code = "MEAL_PLAN_NOT_FOUND", error = "No meal plan to regenerate. Generate one first." });

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(row.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();
        var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(row.GapItemsJson, CacheJson)
                   ?? new List<MealPlanGapItem>();

        var inventory = await db.Products
            .Where(p => p.FridgeId == fridgeId)
            .Select(p => new MealPlanInventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
            .ToListAsync(ct);

        var prefs = await db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage, u.DietaryProfile })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        var dietaryProfile = prefs?.DietaryProfile;

        // Avoid repeating any dish already in the plan (including the one being replaced).
        var avoid = meals.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        var newMeal = await planner.RegenerateMealAsync(inventory, cuisinePreference, language, day, normalizedMealType, avoid, dietaryProfile, ct);
        if (newMeal is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Replace ONLY the meal matching the given day and mealType
        meals.RemoveAll(m => string.Equals(m.Day, day, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(m.MealType, normalizedMealType, StringComparison.OrdinalIgnoreCase));
        meals.Add(newMeal);

        var now = DateTime.UtcNow;
        row.MealsJson = JsonSerializer.Serialize(meals, CacheJson);
        var filteredGaps = await FilterGapItemsAsync(db, fridgeId, meals, gaps, ct);
        row.GapItemsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(meals, filteredGaps, now));
    }

    public sealed record GetRecipeRequest(string Day, string MealType);

    private static async Task<IResult> GetRecipeAsync(
        GetRecipeRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        IMealPlannerService planner,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        if (req.Day is null || !Weekdays.TryGetValue(req.Day.Trim(), out var day))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["day"] = ["Day must be one of Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday"]
            });
        }

        if (string.IsNullOrWhiteSpace(req.MealType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mealType"] = ["MealType must be specified (breakfast, lunch, or dinner)"]
            });
        }

        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();
        var fridgeId = resolved.Value.FridgeId;

        var row = await db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
        if (row is null)
            return Results.NotFound(new { code = "MEAL_PLAN_NOT_FOUND", error = "No meal plan yet. Generate one first." });

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(row.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();
        var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(row.GapItemsJson, CacheJson)
                   ?? new List<MealPlanGapItem>();

        var index = meals.FindIndex(m => 
            string.Equals(m.Day, day, StringComparison.OrdinalIgnoreCase) && 
            string.Equals(m.MealType ?? "", req.MealType.Trim(), StringComparison.OrdinalIgnoreCase));
            
        if (index < 0)
            return Results.NotFound(new { code = "MEAL_NOT_FOUND", error = "No meal of that type for that day in the current plan." });

        var meal = meals[index];

        // Already have a recipe (filled earlier or carried by a regenerated meal) — return as-is,
        // no LLM call, no token spend.
        if (meal.Steps is { Count: > 0 })
        {
            var cachedGaps = await FilterGapItemsAsync(db, fridgeId, meals, gaps, ct);
            return Results.Ok(new MealPlanResponse(meals, cachedGaps, row.UpdatedAt));
        }

        var language = SupportedLanguages.Normalize(
            await db.Users.Where(u => u.Id == uid).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(ct));

        var recipe = await planner.GenerateRecipeAsync(meal.Name, meal.Ingredients, language, ct);
        if (recipe is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        meals[index] = meal with
        {
            Description = recipe.Description,
            Steps = recipe.Steps,
            Calories = recipe.Calories,
            Protein = recipe.Protein,
            Fat = recipe.Fat,
            Carbs = recipe.Carbs
        };

        var now = DateTime.UtcNow;
        row.MealsJson = JsonSerializer.Serialize(meals, CacheJson);
        var filteredGaps = await FilterGapItemsAsync(db, fridgeId, meals, gaps, ct);
        row.GapItemsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(meals, filteredGaps, now));
    }

    public sealed record ImportGapsRequest(IReadOnlyList<MealPlanGapItem> Items);
    public sealed record ImportGapsResponse(int Created, int Skipped);

    private static async Task<IResult> ImportGapsAsync(
        ImportGapsRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();
        if (req.Items.Count == 0) return Results.Ok(new ImportGapsResponse(0, 0));

        var fridgeId = resolved.Value.FridgeId;
        var products = await db.Products
            .Where(p => p.FridgeId == fridgeId)
            .ToListAsync(ct);

        var shoppingItems = await db.ShoppingItems
            .Where(i => i.FridgeId == fridgeId && !i.Checked)
            .ToListAsync(ct);

        var created = 0;
        var skipped = 0;

        foreach (var item in req.Items)
        {
            var rawName = item.Name?.Trim();
            if (string.IsNullOrWhiteSpace(rawName)) { skipped++; continue; }

            var parsed = IngredientDeductionHelper.Parse(rawName, item.Quantity, item.Unit);
            var (isCovered, missingQty, unit) = IngredientDeductionHelper.CalculateMissing(parsed, products, shoppingItems);

            if (isCovered)
            {
                skipped++;
                continue;
            }

            var category = CategoryInferrer.InferCategory(parsed.CleanName, item.Category);

            var newShoppingItem = new ShoppingItem
            {
                UserId = uid,
                FridgeId = fridgeId,
                Name = parsed.CleanName,
                Quantity = missingQty ?? parsed.Quantity,
                Unit = unit ?? item.Unit,
                Category = category
            };

            db.ShoppingItems.Add(newShoppingItem);
            shoppingItems.Add(newShoppingItem);
            created++;
        }

        if (created > 0) await db.SaveChangesAsync(ct);
        return Results.Ok(new ImportGapsResponse(created, skipped));
    }

    private static async Task<List<MealPlanGapItem>> FilterGapItemsAsync(
        VFridgeDbContext db,
        int fridgeId,
        IReadOnlyList<MealPlanMeal> meals,
        List<MealPlanGapItem> gaps,
        CancellationToken ct)
    {
        var products = await db.Products
            .Where(p => p.FridgeId == fridgeId)
            .ToListAsync(ct);

        var shoppingItems = await db.ShoppingItems
            .Where(i => i.FridgeId == fridgeId && !i.Checked)
            .ToListAsync(ct);

        // Collect all ingredient names across active meals
        var allMealIngredientTexts = meals
            .SelectMany(m => m.Ingredients ?? new List<string>())
            .Where(ing => !string.IsNullOrWhiteSpace(ing))
            .ToList();

        var result = new List<MealPlanGapItem>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in gaps)
        {
            if (string.IsNullOrWhiteSpace(g.Name)) continue;

            var parsed = IngredientDeductionHelper.Parse(g.Name, g.Quantity, g.Unit);

            // CRITICAL CHECK: Gap item MUST actually belong to at least one active meal's ingredient list!
            bool isNeededInAnyMeal = allMealIngredientTexts.Any(ing => 
                IngredientDeductionHelper.IsNameMatch(ing, parsed.CleanName));

            if (!isNeededInAnyMeal)
            {
                // Discard orphaned gap that is not part of any active meal!
                continue;
            }

            var (isCovered, missingQty, unit) = IngredientDeductionHelper.CalculateMissing(parsed, products, shoppingItems);
            if (isCovered) continue;

            var finalQtyStr = missingQty?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? g.Quantity;
            var category = CategoryInferrer.InferCategory(parsed.CleanName, g.Category);

            if (seenNames.Add(parsed.CleanName))
            {
                result.Add(new MealPlanGapItem(parsed.CleanName, finalQtyStr, unit ?? g.Unit, category));
            }
        }

        return result;
    }
}
