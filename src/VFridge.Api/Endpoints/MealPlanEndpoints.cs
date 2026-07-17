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
        var filteredGaps = await FilterGapItemsAsync(db, resolved.Value.FridgeId, gaps, ct);
        return Results.Ok(new MealPlanResponse(meals, filteredGaps, row.UpdatedAt));
    }

    private static async Task<IResult> GenerateAsync(
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

        // Steer the plan by the same stored preferences the chef uses, so the two stay
        // consistent (a Ukrainian-cuisine user gets borscht, not random tacos) and the plan
        // is written in the user's language. Read from the user record, not Accept-Language.
        var prefs = await db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);

        var plan = await planner.GenerateAsync(inventory, cuisinePreference, language, ct);
        if (plan is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Upsert the cached row for this fridge. We serialize meals + gaps
        // separately so a future migration could pivot to typed columns
        // without rewriting blobs in two places.
        var mealsJson = JsonSerializer.Serialize(plan.Meals, CacheJson);
        var gapsJson = JsonSerializer.Serialize(plan.GapItems, CacheJson);
        var now = DateTime.UtcNow;

        var existing = await db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
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

        var filteredGaps = await FilterGapItemsAsync(db, fridgeId, plan.GapItems.ToList(), ct);
        return Results.Ok(plan with { GapItems = filteredGaps, GeneratedAt = now });
    }

    public sealed record RegenerateDayRequest(string Day);

    // The five weekdays the planner assigns meals to. Case-insensitive match; the canonical
    // English code is what gets stored and pinned on the regenerated meal.
    private static readonly Dictionary<string, string> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monday"] = "Monday",
        ["tuesday"] = "Tuesday",
        ["wednesday"] = "Wednesday",
        ["thursday"] = "Thursday",
        ["friday"] = "Friday",
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
                ["day"] = ["Day must be one of Monday, Tuesday, Wednesday, Thursday, Friday"]
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
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);

        // Avoid repeating any dish already in the plan (including the one being replaced).
        var avoid = meals.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        var newMeals = await planner.RegenerateDayAsync(inventory, cuisinePreference, language, day, avoid, ct);
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

        var filteredGaps = await FilterGapItemsAsync(db, fridgeId, gaps, ct);
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
                ["day"] = ["Day must be one of Monday, Tuesday, Wednesday, Thursday, Friday"]
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
            return Results.Ok(new MealPlanResponse(meals, gaps, row.UpdatedAt));

        var language = SupportedLanguages.Normalize(
            await db.Users.Where(u => u.Id == uid).Select(u => u.PreferredLanguage).FirstOrDefaultAsync(ct));

        var recipe = await planner.GenerateRecipeAsync(meal.Name, meal.Ingredients, language, ct);
        if (recipe is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        meals[index] = meal with { Description = recipe.Description, Steps = recipe.Steps };

        var now = DateTime.UtcNow;
        row.MealsJson = JsonSerializer.Serialize(meals, CacheJson);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var filteredGaps = await FilterGapItemsAsync(db, fridgeId, gaps, ct);
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
        var existing = await db.ShoppingItems
            .Where(i => i.FridgeId == fridgeId && !i.Checked)
            .Select(i => i.Name.ToLower())
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var created = 0;
        var skipped = 0;
        foreach (var item in req.Items)
        {
            var trimmed = item.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) { skipped++; continue; }
            if (existingSet.Contains(trimmed.ToLower())) { skipped++; continue; }

            var category = ProductCategories.IsValid(item.Category) ? item.Category : ProductCategories.Other;
            decimal? qty = null;
            if (item.Quantity is { } q && decimal.TryParse(q, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                qty = parsed;
            }

            db.ShoppingItems.Add(new ShoppingItem
            {
                UserId = uid,
                FridgeId = fridgeId,
                Name = trimmed,
                Quantity = qty,
                Unit = item.Unit,
                Category = category
            });
            existingSet.Add(trimmed.ToLower());
            created++;
        }

        if (created > 0) await db.SaveChangesAsync(ct);
        return Results.Ok(new ImportGapsResponse(created, skipped));
    }

    private static async Task<List<MealPlanGapItem>> FilterGapItemsAsync(
        VFridgeDbContext db,
        int fridgeId,
        List<MealPlanGapItem> gaps,
        CancellationToken ct)
    {
        var productNames = await db.Products
            .Where(p => p.FridgeId == fridgeId)
            .Select(p => p.Name.ToLower().Trim())
            .ToListAsync(ct);

        return gaps
            .Where(g => !productNames.Contains(g.Name.ToLower().Trim()))
            .ToList();
    }
}
