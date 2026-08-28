using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Features.Products;

public class ProductsService : IProductsService
{
    private readonly VFridgeDbContext _db;
    private readonly FridgeContext _fridgeContext;
    private readonly ICurrentUser _me;

    public ProductsService(VFridgeDbContext db, FridgeContext fridgeContext, ICurrentUser me)
    {
        _db = db;
        _fridgeContext = fridgeContext;
        _me = me;
    }

    public async Task<IResult> ListAsync(CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var items = await _db.Products
            .Where(p => p.FridgeId == resolved.Value.FridgeId)
            .OrderBy(p => p.ExpiryDate)
            .Select(p => new ProductResponse(
                p.Id,
                p.Name,
                p.Description,
                p.Quantity,
                p.Unit,
                p.ExpiryDate,
                p.Category,
                p.OwnerId,
                p.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    public async Task<IResult> CreateAsync(CreateProductRequest req, CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null || _me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var category = req.Category is { } c && ProductCategories.IsValid(c) ? c : ProductCategories.Other;
        var trimmedName = req.Name.Trim();
        var trimmedUnit = req.Unit.Trim();

        // Separate batch policy: only merge if Name, Unit AND ExpiryDate match exactly
        var existingProduct = await _db.Products
            .FirstOrDefaultAsync(p =>
                p.FridgeId == resolved.Value.FridgeId &&
                p.Name.ToLower() == trimmedName.ToLower() &&
                p.Unit.ToLower() == trimmedUnit.ToLower() &&
                p.ExpiryDate == req.ExpiryDate, ct);

        Product entity;
        if (existingProduct is not null)
        {
            existingProduct.Quantity += req.Quantity;
            if (!string.IsNullOrWhiteSpace(req.Description))
            {
                existingProduct.Description = req.Description.Trim();
            }
            entity = existingProduct;
        }
        else
        {
            entity = new Product
            {
                Name = trimmedName,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                Quantity = req.Quantity,
                Unit = trimmedUnit,
                ExpiryDate = req.ExpiryDate,
                Category = category,
                OwnerId = uid,
                FridgeId = resolved.Value.FridgeId
            };
            _db.Products.Add(entity);
        }

        // Clean up matching unchecked shopping items since product is now in the fridge
        var matchingShoppingItems = await _db.ShoppingItems
            .Where(i =>
                i.FridgeId == resolved.Value.FridgeId &&
                !i.Checked &&
                i.Name.ToLower() == trimmedName.ToLower())
            .ToListAsync(ct);

        if (matchingShoppingItems.Count > 0)
        {
            _db.ShoppingItems.RemoveRange(matchingShoppingItems);
        }

        await _db.SaveChangesAsync(ct);

        var resp = new ProductResponse(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Quantity,
            entity.Unit,
            entity.ExpiryDate,
            entity.Category,
            entity.OwnerId,
            entity.CreatedAt);

        return Results.Created($"/products/{entity.Id}", resp);
    }

    public async Task<IResult> UpdateAsync(int id, UpdateProductRequest req, CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null)
            return Results.NotFound(new { code = "PRODUCT_NOT_FOUND", error = "Product not found" });

        if (req.Name is { } n)
        {
            if (n.Trim().Length < 2)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name is too short"]
                });
            }
            entity.Name = n.Trim();
        }

        if (req.Description is not null)
        {
            entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        }

        // Quantity going to 0 is an auto-deletion signal with consumption logging
        var quantityDroppedToZero = req.Quantity is { } q0 && q0 <= 0;
        if (quantityDroppedToZero)
        {
            _db.ConsumptionLogs.Add(BuildConsumptionLog(entity, ConsumptionStatus.Consumed));
            _db.Products.Remove(entity);
            await _db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true, removed = true });
        }

        if (req.Quantity is { } q)
        {
            if (q <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["quantity"] = ["Quantity must be greater than 0"]
                });
            }
            entity.Quantity = q;
        }

        if (req.Unit is { } u) entity.Unit = u;
        if (req.ExpiryDate is { } d) entity.ExpiryDate = d;
        if (req.Category is { } cat)
        {
            if (!ProductCategories.IsValid(cat))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["category"] = ["Unknown category"]
                });
            }
            entity.Category = cat;
        }

        await _db.SaveChangesAsync(ct);

        var resp = new ProductResponse(
            entity.Id,
            entity.Name,
            entity.Description,
            entity.Quantity,
            entity.Unit,
            entity.ExpiryDate,
            entity.Category,
            entity.OwnerId,
            entity.CreatedAt);

        return Results.Ok(resp);
    }

    public async Task<IResult> CookAsync(CookRecipeRequest req, CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null || _me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridgeId = resolved.Value.FridgeId;
        var fridgeProducts = await _db.Products
            .Where(p => p.FridgeId == fridgeId)
            .ToListAsync(ct);

        var ingredientsToDeduct = new List<string>();
        if (req.Ingredients is { Count: > 0 })
        {
            ingredientsToDeduct.AddRange(req.Ingredients);
        }
        else if (req.SavedRecipeId is int savedId)
        {
            var savedRecipe = await _db.SavedRecipes.FirstOrDefaultAsync(r => r.Id == savedId && r.UserId == uid, ct);
            if (savedRecipe is not null)
            {
                var ings = System.Text.Json.JsonSerializer.Deserialize<List<string>>(savedRecipe.IngredientsJson);
                if (ings is not null) ingredientsToDeduct.AddRange(ings);
            }
        }

        // Strict ingredient verification
        var missingRequired = new List<string>();
        foreach (var rawIng in ingredientsToDeduct)
        {
            if (string.IsNullOrWhiteSpace(rawIng)) continue;

            var parsed = IngredientDeductionHelper.Parse(rawIng);
            var (isCovered, missingQty, unit) = IngredientDeductionHelper.CalculateMissing(parsed, fridgeProducts, []);

            if (!isCovered)
            {
                var isOptional = IngredientDeductionHelper.IsOptionalSeasoningOrSauce(parsed.CleanName);
                if (!isOptional || !req.IgnoreOptionalMissing)
                {
                    missingRequired.Add(missingQty.HasValue ? $"{missingQty.Value}{unit} {parsed.CleanName}" : parsed.CleanName);
                }
            }
        }

        if (missingRequired.Count > 0 && !req.IgnoreOptionalMissing && req.Ingredients is { Count: > 0 })
        {
            // If strictly required ingredients are missing
            var missingList = string.Join(", ", missingRequired);
            return Results.BadRequest(new
            {
                code = "MISSING_REQUIRED_INGREDIENTS",
                error = $"Не вистачає інгредієнтів для приготування: {missingList}",
                missing = missingRequired
            });
        }

        var deductions = new List<DeductedIngredientSummary>();

        // Deduct matching ingredients from fridge inventory
        foreach (var rawIng in ingredientsToDeduct)
        {
            if (string.IsNullOrWhiteSpace(rawIng)) continue;

            var parsed = IngredientDeductionHelper.Parse(rawIng);
            var matching = fridgeProducts.FirstOrDefault(p => IngredientDeductionHelper.IsNameMatch(p.Name, parsed.CleanName));
            if (matching is null) continue;

            decimal deductAmount;
            if (parsed.Quantity is { } neededQty && neededQty > 0)
            {
                deductAmount = IngredientDeductionHelper.ConvertQuantity(neededQty, parsed.Unit, matching.Unit);
            }
            else
            {
                deductAmount = matching.Quantity >= 1 ? 1 : matching.Quantity;
            }

            bool fullyConsumed;
            if (matching.Quantity <= deductAmount)
            {
                fullyConsumed = true;
                deductAmount = matching.Quantity;
                _db.Products.Remove(matching);
                fridgeProducts.Remove(matching);
                _db.ConsumptionLogs.Add(BuildConsumptionLog(matching, ConsumptionStatus.Consumed));
            }
            else
            {
                fullyConsumed = false;
                matching.Quantity -= deductAmount;
            }

            deductions.Add(new DeductedIngredientSummary(
                rawIng,
                matching.Name,
                deductAmount,
                matching.Unit,
                fullyConsumed));
        }

        // Prepare KBJU info
        var cal = req.CaloriesPerPortion ?? 0;
        var prot = req.ProteinPerPortion ?? 0;
        var fat = req.FatPerPortion ?? 0;
        var carbs = req.CarbsPerPortion ?? 0;

        if (cal == 0 && prot == 0 && fat == 0 && carbs == 0 && !string.IsNullOrWhiteSpace(req.Description))
        {
            var parsedNutrition = IngredientDeductionHelper.ParseNutrition(req.Description);
            cal = parsedNutrition.Calories;
            prot = parsedNutrition.Protein;
            fat = parsedNutrition.Fat;
            carbs = parsedNutrition.Carbs;
        }

        string descText = req.Description?.Trim() ?? string.Empty;
        if (cal > 0 || prot > 0 || fat > 0 || carbs > 0)
        {
            var kbjuStr = $"КБЖВ на 1 порцію: {cal} кКал | Б: {prot}г | Ж: {fat}г | В: {carbs}г";
            if (!descText.Contains("КБЖВ") && !descText.Contains("кКал"))
            {
                descText = string.IsNullOrWhiteSpace(descText) ? kbjuStr : $"{descText} • {kbjuStr}";
            }
        }

        var expiryDays = req.ExpiryDays.GetValueOrDefault(3);
        if (expiryDays <= 0) expiryDays = 3;
        var expDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(expiryDays));

        // Create or update prepared meal in fridge
        var existingPrepared = fridgeProducts.FirstOrDefault(p =>
            p.Name.Equals(req.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
            p.Category == ProductCategories.PreparedMeals);

        Product entity;
        if (existingPrepared is not null)
        {
            existingPrepared.Quantity += req.Portions;
            existingPrepared.ExpiryDate = expDate;
            if (!string.IsNullOrWhiteSpace(descText))
                existingPrepared.Description = descText;
            entity = existingPrepared;
        }
        else
        {
            entity = new Product
            {
                Name = req.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(descText) ? null : descText,
                Quantity = req.Portions,
                Unit = "порцій",
                ExpiryDate = expDate,
                Category = ProductCategories.PreparedMeals,
                OwnerId = uid,
                FridgeId = fridgeId
            };
            _db.Products.Add(entity);
        }

        await _db.SaveChangesAsync(ct);

        var productResp = new ProductResponse(
            entity.Id, entity.Name, entity.Description, entity.Quantity, entity.Unit,
            entity.ExpiryDate, entity.Category, entity.OwnerId, entity.CreatedAt);

        var message = deductions.Count > 0
            ? $"Приготовано {req.Portions} порц. «{entity.Name}». Списано {deductions.Count} інгредієнтів з холодильника."
            : $"Приготовано {req.Portions} порц. «{entity.Name}» та додано до холодильника.";

        return Results.Ok(new CookRecipeResponse(productResp, deductions, message));
    }

    public async Task<IResult> ConsumeAsync(int id, ConsumeProductRequest req, CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null || _me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null)
            return Results.NotFound(new { code = "PRODUCT_NOT_FOUND", error = "Product not found" });

        if (entity.Quantity <= 0)
        {
            _db.Products.Remove(entity);
            await _db.SaveChangesAsync(ct);
            return Results.NotFound(new { code = "PORTIONS_EXHAUSTED", error = "Ця страва вже повністю з'їдена або вилучена з холодильника." });
        }

        var portionsToConsume = req.Portions <= 0 ? 1 : req.Portions;

        // Auto-detect meal type if not provided
        string mealType;
        if (!string.IsNullOrWhiteSpace(req.MealType))
        {
            mealType = req.MealType.Trim().ToLowerInvariant();
        }
        else
        {
            var hour = DateTime.UtcNow.AddHours(3).Hour; // Kyiv local time UTC+3
            mealType = hour switch
            {
                >= 6 and < 11 => "breakfast",
                >= 11 and < 17 => "lunch",
                >= 17 and < 22 => "dinner",
                _ => "snack"
            };
        }

        DateOnly date;
        if (!string.IsNullOrWhiteSpace(req.Date) && DateOnly.TryParse(req.Date, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            date = d;
        }
        else
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        // Determine KBJU per portion
        int calPerPortion = req.Calories ?? 0;
        decimal protPerPortion = req.Protein ?? 0;
        decimal fatPerPortion = req.Fat ?? 0;
        decimal carbsPerPortion = req.Carbs ?? 0;

        if (calPerPortion == 0 && protPerPortion == 0 && fatPerPortion == 0 && carbsPerPortion == 0)
        {
            var parsed = IngredientDeductionHelper.ParseNutrition(entity.Description);
            calPerPortion = parsed.Calories;
            protPerPortion = parsed.Protein;
            fatPerPortion = parsed.Fat;
            carbsPerPortion = parsed.Carbs;
        }

        bool productRemoved;
        decimal remaining;

        if (entity.Quantity <= portionsToConsume)
        {
            productRemoved = true;
            remaining = 0;
            _db.ConsumptionLogs.Add(BuildConsumptionLog(entity, ConsumptionStatus.Consumed));
            _db.Products.Remove(entity);
        }
        else
        {
            productRemoved = false;
            entity.Quantity = Math.Max(0, entity.Quantity - portionsToConsume);
            remaining = entity.Quantity;
        }

        // Add to NutritionLogs
        var nutritionLog = new NutritionLog
        {
            UserId = uid,
            Date = date,
            MealType = mealType,
            FoodName = entity.Name,
            Quantity = portionsToConsume,
            Unit = entity.Unit,
            Calories = (int)Math.Round(calPerPortion * portionsToConsume),
            Protein = Math.Round(protPerPortion * portionsToConsume, 2),
            Fat = Math.Round(fatPerPortion * portionsToConsume, 2),
            Carbs = Math.Round(carbsPerPortion * portionsToConsume, 2),
            LoggedAt = DateTime.UtcNow
        };

        _db.NutritionLogs.Add(nutritionLog);
        await _db.SaveChangesAsync(ct);

        var message = productRemoved
            ? $"З'їдено останню порцію «{entity.Name}». Страву вилучено з холодильника та внесено в щоденник харчування."
            : $"З'їдено {portionsToConsume} порц. «{entity.Name}» (залишилось {remaining} {entity.Unit}). Запис додано в щоденник харчування.";

        return Results.Ok(new ConsumeProductResponse(productRemoved, remaining, nutritionLog.Id, message));
    }

    public async Task<IResult> DeleteAsync(int id, CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null)
            return Results.NotFound(new { code = "PRODUCT_NOT_FOUND", error = "Product not found" });

        _db.ConsumptionLogs.Add(BuildConsumptionLog(entity, ClassifyDelete(entity)));
        _db.Products.Remove(entity);
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true });
    }

    public async Task<IResult> DeleteAllAsync(CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        if (!string.Equals(resolved.Value.Role, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { code = "NOT_FRIDGE_OWNER", error = "Only the fridge owner can empty the entire fridge" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var owned = await _db.Products.Where(p => p.FridgeId == resolved.Value.FridgeId).ToListAsync(ct);
        foreach (var p in owned)
            _db.ConsumptionLogs.Add(BuildConsumptionLog(p, ClassifyDelete(p)));

        _db.Products.RemoveRange(owned);
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true, deleted = owned.Count });
    }

    private static ConsumptionLog BuildConsumptionLog(Product entity, string status)
    {
        int? ageDays = null;
        if (entity.CreatedAt is { } created)
            ageDays = (int)Math.Max(0, (DateTime.UtcNow - created).TotalDays);

        return new ConsumptionLog
        {
            UserId = entity.OwnerId,
            FridgeId = entity.FridgeId,
            ProductName = entity.Name,
            Quantity = entity.Quantity,
            Unit = entity.Unit,
            Category = entity.Category,
            Status = status,
            AgeDays = ageDays
        };
    }

    private static string ClassifyDelete(Product entity)
    {
        if (entity.ExpiryDate is { } exp && exp < DateOnly.FromDateTime(DateTime.UtcNow))
            return ConsumptionStatus.Expired;
        return ConsumptionStatus.Wasted;
    }

    private static bool TryValidate<T>(T instance, out Dictionary<string, string[]> errors) where T : class
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true);
        errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(""), (r, m) => (m, r.ErrorMessage ?? "Invalid"))
            .GroupBy(t => t.m)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Item2).ToArray());
        return ok;
    }
}
