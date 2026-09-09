using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Features.MealPlanning;

public class MealPlanService : IMealPlanService
{
    private static readonly JsonSerializerOptions CacheJson = new(JsonSerializerDefaults.Web);

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

    private readonly VFridgeDbContext _db;
    private readonly ICurrentUser _me;
    private readonly FridgeContext _fridgeContext;
    private readonly IMealPlannerService _planner;
    private readonly ILogger<MealPlanService> _logger;

    public MealPlanService(
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        IMealPlannerService planner,
        ILogger<MealPlanService> logger)
    {
        _db = db;
        _me = me;
        _fridgeContext = fridgeContext;
        _planner = planner;
        _logger = logger;
    }

    public async Task<IResult> GetCachedAsync(CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var row = await _db.MealPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.FridgeId == resolved.Value.FridgeId, ct);
        if (row is null) return Results.NoContent();

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(row.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();
        var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(row.GapItemsJson, CacheJson)
                   ?? new List<MealPlanGapItem>();

        string language = "uk";
        if (_me.UserId is int uid)
        {
            var prefs = await _db.Users
                .Where(u => u.Id == uid)
                .Select(u => new { u.PreferredLanguage })
                .FirstOrDefaultAsync(ct);
            language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        }

        meals = await EnrichMealsWithStructuredIngredientsAsync(resolved.Value.FridgeId, meals, language, ct);
        var filteredGaps = await FilterGapItemsAsync(resolved.Value.FridgeId, meals, gaps, ct);
        return Results.Ok(new MealPlanResponse(meals, filteredGaps, row.UpdatedAt));
    }

    public async Task<IResult> GenerateAsync(CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var fridgeId = resolved.Value.FridgeId;
        var inventory = await _db.Products
            .Where(p => p.FridgeId == fridgeId)
            .Select(p => new MealPlanInventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
            .ToListAsync(ct);

        var prefs = await _db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage, u.DietaryProfile })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        var dietaryProfile = prefs?.DietaryProfile;

        var existing = await _db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
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

        var validatedDay = DateTime.UtcNow.DayOfWeek.ToString();

        var plan = await _planner.GenerateAsync(
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

        var candidateGaps = plan.GapItems.ToList();
        if (isSameWeek && existingGaps is not null)
        {
            candidateGaps = existingGaps
                .Concat(plan.GapItems)
                .GroupBy(g => g.Name.ToLower().Trim())
                .Select(grp => grp.First())
                .ToList();
        }

        var enrichedMeals = await EnrichMealsWithStructuredIngredientsAsync(fridgeId, plan.Meals.ToList(), language, ct);
        var filteredGaps = await FilterGapItemsAsync(fridgeId, enrichedMeals, candidateGaps, ct);

        var now = DateTime.UtcNow;
        var mealsJson = JsonSerializer.Serialize(enrichedMeals, CacheJson);
        var gapsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);

        if (existing is null)
        {
            _db.MealPlans.Add(new MealPlanRecord
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
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(enrichedMeals, filteredGaps, now));
    }

    public async Task<IResult> RegenerateDayAsync(RegenerateDayRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (req.Day is null || !Weekdays.TryGetValue(req.Day.Trim(), out var day))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["day"] = ["Day must be a valid weekday name (e.g. Monday)."]
            });
        }

        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var fridgeId = resolved.Value.FridgeId;
        var existing = await _db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
        if (existing is null)
        {
            return Results.Json(
                new ApiError("MEAL_PLAN_NOT_FOUND", "No meal plan exists yet. Generate a full plan first."),
                statusCode: StatusCodes.Status404NotFound);
        }

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(existing.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();
        var avoid = meals.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        var inventory = await _db.Products
            .Where(p => p.FridgeId == fridgeId)
            .Select(p => new MealPlanInventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
            .ToListAsync(ct);

        var prefs = await _db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage, u.DietaryProfile })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        var dietaryProfile = prefs?.DietaryProfile;

        var replacementMeals = await _planner.RegenerateDayAsync(
            inventory,
            cuisinePreference,
            language,
            day,
            avoid,
            dietaryProfile,
            ct);

        if (replacementMeals is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var enrichedReplacements = await EnrichMealsWithStructuredIngredientsAsync(fridgeId, replacementMeals.ToList(), language, ct);
        meals.RemoveAll(m => string.Equals(m.Day, day, StringComparison.OrdinalIgnoreCase));
        meals.AddRange(enrichedReplacements);

        var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(existing.GapItemsJson, CacheJson)
                   ?? new List<MealPlanGapItem>();

        var filteredGaps = await FilterGapItemsAsync(fridgeId, meals, gaps, ct);

        var now = DateTime.UtcNow;
        existing.MealsJson = JsonSerializer.Serialize(meals, CacheJson);
        existing.GapItemsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);
        existing.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(meals, filteredGaps, now));
    }

    public async Task<IResult> RegenerateMealAsync(RegenerateMealRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (req.Day is null || !Weekdays.TryGetValue(req.Day.Trim(), out var day))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["day"] = ["Day must be a valid weekday name (e.g. Monday)."]
            });
        }

        if (string.IsNullOrWhiteSpace(req.MealType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mealType"] = ["MealType cannot be empty."]
            });
        }

        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var fridgeId = resolved.Value.FridgeId;
        var existing = await _db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
        if (existing is null)
        {
            return Results.Json(
                new ApiError("MEAL_PLAN_NOT_FOUND", "No meal plan exists yet. Generate a full plan first."),
                statusCode: StatusCodes.Status404NotFound);
        }

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(existing.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();
        var avoid = meals.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        var inventory = await _db.Products
            .Where(p => p.FridgeId == fridgeId)
            .Select(p => new MealPlanInventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
            .ToListAsync(ct);

        var prefs = await _db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage, u.DietaryProfile })
            .FirstOrDefaultAsync(ct);
        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        var dietaryProfile = prefs?.DietaryProfile;

        var replacementMeal = await _planner.RegenerateMealAsync(
            inventory,
            cuisinePreference,
            language,
            day,
            req.MealType,
            avoid,
            dietaryProfile,
            ct);

        if (replacementMeal is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var enrichedList = await EnrichMealsWithStructuredIngredientsAsync(fridgeId, new List<MealPlanMeal> { replacementMeal }, language, ct);
        meals.RemoveAll(m => string.Equals(m.Day, day, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(m.MealType ?? "dinner", req.MealType, StringComparison.OrdinalIgnoreCase));
        meals.Add(enrichedList[0]);

        var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(existing.GapItemsJson, CacheJson)
                   ?? new List<MealPlanGapItem>();

        var filteredGaps = await FilterGapItemsAsync(fridgeId, meals, gaps, ct);

        var now = DateTime.UtcNow;
        existing.MealsJson = JsonSerializer.Serialize(meals, CacheJson);
        existing.GapItemsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);
        existing.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(meals, filteredGaps, now));
    }

    public async Task<IResult> GetRecipeAsync(GetRecipeRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (req.Day is null || !Weekdays.TryGetValue(req.Day.Trim(), out var day))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["day"] = ["Day must be a valid weekday name (e.g. Monday)."]
            });
        }

        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var fridgeId = resolved.Value.FridgeId;
        var existing = await _db.MealPlans.FirstOrDefaultAsync(p => p.FridgeId == fridgeId, ct);
        if (existing is null)
        {
            return Results.Json(
                new ApiError("MEAL_PLAN_NOT_FOUND", "No meal plan exists yet. Generate a full plan first."),
                statusCode: StatusCodes.Status404NotFound);
        }

        var meals = JsonSerializer.Deserialize<List<MealPlanMeal>>(existing.MealsJson, CacheJson)
                    ?? new List<MealPlanMeal>();

        var mealIndex = meals.FindIndex(m =>
            string.Equals(m.Day, day, StringComparison.OrdinalIgnoreCase) &&
            (req.MealType is null || string.Equals(m.MealType ?? "dinner", req.MealType, StringComparison.OrdinalIgnoreCase)));

        if (mealIndex == -1)
        {
            return Results.Json(
                new ApiError("MEAL_NOT_FOUND", $"The plan has no meal on {day}."),
                statusCode: StatusCodes.Status404NotFound);
        }

        var targetMeal = meals[mealIndex];
        if (targetMeal.Steps is { Count: > 0 } && !string.IsNullOrWhiteSpace(targetMeal.Description))
        {
            var gaps = JsonSerializer.Deserialize<List<MealPlanGapItem>>(existing.GapItemsJson, CacheJson)
                       ?? new List<MealPlanGapItem>();
            return Results.Ok(new MealPlanResponse(meals, gaps, existing.UpdatedAt));
        }

        var prefs = await _db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.PreferredLanguage })
            .FirstOrDefaultAsync(ct);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);

        var recipe = await _planner.GenerateRecipeAsync(targetMeal.Name, targetMeal.Ingredients, language, ct);
        if (recipe is null)
        {
            return Results.Problem(
                title: "The recipe generator is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var targetMealWithRecipe = targetMeal with
        {
            Description = recipe.Description,
            Steps = recipe.Steps,
            Calories = recipe.Calories,
            Protein = recipe.Protein,
            Fat = recipe.Fat,
            Carbs = recipe.Carbs
        };
        var enrichedRecipeMeals = await EnrichMealsWithStructuredIngredientsAsync(fridgeId, new List<MealPlanMeal> { targetMealWithRecipe }, language, ct);
        meals[mealIndex] = enrichedRecipeMeals[0];

        var now = DateTime.UtcNow;
        existing.MealsJson = JsonSerializer.Serialize(meals, CacheJson);
        var gapsList = JsonSerializer.Deserialize<List<MealPlanGapItem>>(existing.GapItemsJson, CacheJson)
                       ?? new List<MealPlanGapItem>();
        var filteredGaps = await FilterGapItemsAsync(fridgeId, meals, gapsList, ct);
        existing.GapItemsJson = JsonSerializer.Serialize(filteredGaps, CacheJson);
        existing.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new MealPlanResponse(meals, filteredGaps, now));
    }

    public async Task<IResult> ImportGapsAsync(ImportGapsRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();
        if (req.Items.Count == 0) return Results.Ok(new ImportGapsResponse(0, 0));

        var fridgeId = resolved.Value.FridgeId;
        var products = await _db.Products
            .Where(p => p.FridgeId == fridgeId)
            .ToListAsync(ct);

        var shoppingItems = await _db.ShoppingItems
            .Where(i => i.FridgeId == fridgeId && !i.Checked)
            .ToListAsync(ct);

        var created = 0;
        var skipped = 0;

        foreach (var item in req.Items)
        {
            var rawName = item.Name?.Trim();
            if (string.IsNullOrWhiteSpace(rawName))
            {
                skipped++;
                continue;
            }

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

            _db.ShoppingItems.Add(newShoppingItem);
            shoppingItems.Add(newShoppingItem);
            created++;
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return Results.Ok(new ImportGapsResponse(created, skipped));
    }

    private async Task<List<MealPlanGapItem>> FilterGapItemsAsync(
        int fridgeId,
        IReadOnlyList<MealPlanMeal> meals,
        List<MealPlanGapItem> gaps,
        CancellationToken ct)
    {
        var products = await _db.Products
            .Where(p => p.FridgeId == fridgeId)
            .ToListAsync(ct);

        var shoppingItems = await _db.ShoppingItems
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

            // Gap item MUST actually belong to at least one active meal's ingredient list
            bool isNeededInAnyMeal = allMealIngredientTexts.Any(ing =>
                IngredientDeductionHelper.IsNameMatch(ing, parsed.CleanName));

            if (!isNeededInAnyMeal)
            {
                // Discard orphaned gap
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

    private static bool IsSameCalendarWeek(DateTime date1, DateTime date2)
    {
        return System.Globalization.ISOWeek.GetWeekOfYear(date1) == System.Globalization.ISOWeek.GetWeekOfYear(date2) &&
               System.Globalization.ISOWeek.GetYear(date1) == System.Globalization.ISOWeek.GetYear(date2);
    }

    private async Task<List<MealPlanMeal>> EnrichMealsWithStructuredIngredientsAsync(
        int fridgeId,
        List<MealPlanMeal> meals,
        string language,
        CancellationToken ct)
    {
        var products = await _db.Products
            .Where(p => p.FridgeId == fridgeId)
            .ToListAsync(ct);

        var enriched = new List<MealPlanMeal>(meals.Count);
        foreach (var meal in meals)
        {
            var structured = new List<RecipeIngredientDto>();
            var displayIngredients = new List<string>();

            foreach (var rawIng in meal.Ingredients ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(rawIng)) continue;

                var parsed = IngredientDeductionHelper.Parse(rawIng, null, null);
                var category = CategoryInferrer.InferCategory(parsed.CleanName);
                var isCovered = products.Any(p => IngredientDeductionHelper.IsNameMatch(p.Name, parsed.CleanName));

                var unitNorm = UnitStandards.Normalize(parsed.Unit);
                if (string.IsNullOrWhiteSpace(unitNorm))
                {
                    unitNorm = "pcs";
                }

                var qty = parsed.Quantity ?? 1m;
                // If quantity is absurdly large (e.g. 1 kg nuts/salt/sugar in a single meal)
                if (qty >= 1m && (unitNorm == "kg" || (qty >= 500m && unitNorm == "g")))
                {
                    if (category is ProductCategories.Snacks or ProductCategories.Pantry or ProductCategories.Sauces)
                    {
                        qty = 30m;
                        unitNorm = "g";
                    }
                }

                var displayUnit = UnitStandards.ToDisplayUnit(unitNorm, language);
                var cleanName = parsed.CleanName;
                if (!string.IsNullOrWhiteSpace(cleanName))
                {
                    cleanName = char.ToUpper(cleanName[0]) + cleanName[1..];
                }

                var displayStr = $"{qty} {displayUnit} {cleanName}";
                displayIngredients.Add(displayStr);

                structured.Add(new RecipeIngredientDto(
                    cleanName,
                    qty,
                    displayUnit,
                    category,
                    isCovered
                ));
            }

            enriched.Add(meal with
            {
                Ingredients = displayIngredients.Count > 0 ? displayIngredients : (meal.Ingredients ?? (IReadOnlyList<string>)Array.Empty<string>()),
                StructuredIngredients = structured
            });
        }

        return enriched;
    }
}
