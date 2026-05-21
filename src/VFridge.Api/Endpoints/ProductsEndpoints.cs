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
            .WithSummary("Empty the active fridge")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

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

        var entity = new Product
        {
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Quantity = req.Quantity,
            Unit = req.Unit,
            ExpiryDate = req.ExpiryDate,
            Category = category,
            OwnerId = uid,
            FridgeId = resolved.Value.FridgeId
        };

        db.Products.Add(entity);
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
