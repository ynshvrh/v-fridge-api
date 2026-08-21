using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        group.MapGet("/", ListAsync)
            .WithName("ListProducts")
            .WithSummary("List the active fridge's products")
            .WithDescription("Ordered by expiry date ascending. Uses the X-Fridge-Id header to pick a fridge or falls back to the caller's first owned fridge.")
            .Produces<List<ProductResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .WithName("CreateProduct")
            .WithSummary("Add a new product to the active fridge")
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/cook", CookAsync)
            .WithName("CookRecipe")
            .WithSummary("Cook a recipe: deduct raw ingredients from fridge and add prepared meal container")
            .Produces<CookRecipeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/{id:int}/consume", ConsumeAsync)
            .WithName("ConsumeProduct")
            .WithSummary("Consume a portion of a product/prepared meal and automatically log to nutrition diary")
            .Produces<ConsumeProductResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/{id:int}/eat", ConsumeAsync)
            .WithName("EatProduct")
            .WithSummary("Alias for consume product/prepared meal")
            .Produces<ConsumeProductResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}", UpdateAsync)
            .WithName("UpdateProduct")
            .WithSummary("Patch a product in the active fridge")
            .Produces<ProductResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteProduct")
            .WithSummary("Delete a product in the active fridge")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/", DeleteAllAsync)
            .WithName("DeleteAllProducts")
            .WithSummary("Empty the active fridge (Owner only)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ApiError>(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListAsync(VFridgeDbContext db, FridgeContext fridgeContext, CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var items = await db.Products
            .Where(p => p.FridgeId == resolved.Value.FridgeId)
            .OrderBy(p => p.ExpiryDate)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Description, p.Quantity, p.Unit, p.ExpiryDate, p.Category, p.OwnerId, p.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        CreateProductRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null || me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var category = req.Category is { } c && ProductCategories.IsValid(c) ? c : ProductCategories.Other;

        var existingProduct = await db.Products
            .FirstOrDefaultAsync(p => 
                p.FridgeId == resolved.Value.FridgeId && 
                p.Name.ToLower() == req.Name.ToLower().Trim() && 
                p.Unit.ToLower() == req.Unit.ToLower().Trim(), ct);

        Product entity;
        if (existingProduct is not null)
        {
            existingProduct.Quantity += req.Quantity;
            if (req.ExpiryDate is { } expDate)
            {
                existingProduct.ExpiryDate = expDate;
            }
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
                Name = req.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                Quantity = req.Quantity,
                Unit = req.Unit.Trim(),
                ExpiryDate = req.ExpiryDate,
                Category = category,
                OwnerId = uid,
                FridgeId = resolved.Value.FridgeId
            };
            db.Products.Add(entity);
        }

        // Clean up matching unchecked shopping items since product is now in the fridge
        var matchingShoppingItems = await db.ShoppingItems
            .Where(i => 
                i.FridgeId == resolved.Value.FridgeId && 
                !i.Checked && 
                i.Name.ToLower() == req.Name.ToLower().Trim())
            .ToListAsync(ct);

        if (matchingShoppingItems.Count > 0)
        {
            db.ShoppingItems.RemoveRange(matchingShoppingItems);
        }

        await db.SaveChangesAsync(ct);

        var resp = new ProductResponse(entity.Id, entity.Name, entity.Description, entity.Quantity, entity.Unit, entity.ExpiryDate, entity.Category, entity.OwnerId, entity.CreatedAt);
        return Results.Created($"/products/{entity.Id}", resp);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateProductRequest req,
        VFridgeDbContext db,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null) return Results.NotFound(new { code = "PRODUCT_NOT_FOUND", error = "Product not found" });

        if (req.Name is { } n)
        {
            if (n.Trim().Length < 2) return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is too short"]
            });
            entity.Name = n.Trim();
        }
        if (req.Description is not null) entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();

        // Quantity going to 0 is a real "I finished this" signal — log it before the row goes away.
        var quantityDroppedToZero = req.Quantity is { } q0 && q0 <= 0;
        if (quantityDroppedToZero)
        {
            db.ConsumptionLogs.Add(BuildConsumptionLog(entity, ConsumptionStatus.Consumed));
            db.Products.Remove(entity);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true, removed = true });
        }

        if (req.Quantity is { } q)
        {
            if (q <= 0) return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["quantity"] = ["Quantity must be greater than 0"]
            });
            entity.Quantity = q;
        }
        if (req.Unit is { } u) entity.Unit = u;
        if (req.ExpiryDate is { } d) entity.ExpiryDate = d;
        if (req.Category is { } cat)
        {
            if (!ProductCategories.IsValid(cat))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["category"] = ["Unknown category"]
                });
            entity.Category = cat;
        }

        await db.SaveChangesAsync(ct);
        var resp = new ProductResponse(entity.Id, entity.Name, entity.Description, entity.Quantity, entity.Unit, entity.ExpiryDate, entity.Category, entity.OwnerId, entity.CreatedAt);
        return Results.Ok(resp);
    }

    private static async Task<IResult> CookAsync(
        CookRecipeRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null || me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridgeId = resolved.Value.FridgeId;
        var fridgeProducts = await db.Products
            .Where(p => p.FridgeId == fridgeId)
            .ToListAsync(ct);

        var deductions = new List<DeductedIngredientSummary>();

        var ingredientsToDeduct = new List<string>();
        if (req.Ingredients is { Count: > 0 })
        {
            ingredientsToDeduct.AddRange(req.Ingredients);
        }
        else if (req.SavedRecipeId is int savedId)
        {
            var savedRecipe = await db.SavedRecipes.FirstOrDefaultAsync(r => r.Id == savedId && r.UserId == uid, ct);
            if (savedRecipe is not null)
            {
                var ings = System.Text.Json.JsonSerializer.Deserialize<List<string>>(savedRecipe.IngredientsJson);
                if (ings is not null) ingredientsToDeduct.AddRange(ings);
            }
        }

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
                db.Products.Remove(matching);
                fridgeProducts.Remove(matching);
                db.ConsumptionLogs.Add(BuildConsumptionLog(matching, ConsumptionStatus.Consumed));
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
            db.Products.Add(entity);
        }

        await db.SaveChangesAsync(ct);

        var productResp = new ProductResponse(
            entity.Id, entity.Name, entity.Description, entity.Quantity, entity.Unit,
            entity.ExpiryDate, entity.Category, entity.OwnerId, entity.CreatedAt);

        var message = deductions.Count > 0
            ? $"Приготовано {req.Portions} порц. «{entity.Name}». Списано {deductions.Count} інгредієнтів з холодильника."
            : $"Приготовано {req.Portions} порц. «{entity.Name}» та додано до холодильника.";

        return Results.Ok(new CookRecipeResponse(productResp, deductions, message));
    }

    private static async Task<IResult> ConsumeAsync(
        int id,
        ConsumeProductRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null || me.UserId is not int uid) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null)
            return Results.NotFound(new { code = "PRODUCT_NOT_FOUND", error = "Product not found" });

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
            db.ConsumptionLogs.Add(BuildConsumptionLog(entity, ConsumptionStatus.Consumed));
            db.Products.Remove(entity);
        }
        else
        {
            productRemoved = false;
            entity.Quantity -= portionsToConsume;
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

        db.NutritionLogs.Add(nutritionLog);
        await db.SaveChangesAsync(ct);

        var message = productRemoved
            ? $"З'їдено останню порцію «{entity.Name}». Страву вилучено з холодильника та внесено в щоденник харчування."
            : $"З'їдено {portionsToConsume} порц. «{entity.Name}» (залишилось {remaining} {entity.Unit}). Запис додано в щоденник харчування.";

        return Results.Ok(new ConsumeProductResponse(productRemoved, remaining, nutritionLog.Id, message));
    }

    private static async Task<IResult> DeleteAsync(int id, VFridgeDbContext db, FridgeContext fridgeContext, CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == id && p.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null)
            return Results.NotFound(new { code = "PRODUCT_NOT_FOUND", error = "Product not found" });

        db.ConsumptionLogs.Add(BuildConsumptionLog(entity, ClassifyDelete(entity)));
        db.Products.Remove(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> DeleteAllAsync(VFridgeDbContext db, FridgeContext fridgeContext, CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        if (!string.Equals(resolved.Value.Role, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { code = "NOT_FRIDGE_OWNER", error = "Only the fridge owner can empty the entire fridge" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var owned = await db.Products.Where(p => p.FridgeId == resolved.Value.FridgeId).ToListAsync(ct);
        foreach (var p in owned)
            db.ConsumptionLogs.Add(BuildConsumptionLog(p, ClassifyDelete(p)));
        db.Products.RemoveRange(owned);
        await db.SaveChangesAsync(ct);

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
