using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class ShoppingEndpoints
{
    public static IEndpointRouteBuilder MapShoppingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shopping").WithTags("Shopping");

        group.MapGet("/", ListAsync)
            .WithName("ListShoppingItems")
            .WithSummary("List the caller's shopping items")
            .WithDescription("Ordered with unchecked items first, then by created_at ascending.")
            .Produces<List<ShoppingItemResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .WithName("CreateShoppingItem")
            .WithSummary("Add an item to the shopping list")
            .Produces<ShoppingItemResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}", UpdateAsync)
            .WithName("UpdateShoppingItem")
            .WithSummary("Patch one of the caller's shopping items")
            .Produces<ShoppingItemResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteShoppingItem")
            .WithSummary("Delete one of the caller's shopping items")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:int}/purchase", PurchaseAsync)
            .WithName("PurchaseShoppingItem")
            .WithSummary("Mark a shopping item as purchased and move it into the fridge")
            .WithDescription("Atomically: deletes the shopping_items row and creates a corresponding products row owned by the same user. The new product carries the shopping item's name, quantity, unit, and category. ExpiryDate is taken from the request body (optional).")
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> ListAsync(
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        if (me.UserId is not int _) return Results.Unauthorized();
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var items = await db.ShoppingItems
            .Where(i => i.FridgeId == resolved.Value.FridgeId)
            .OrderBy(i => i.Checked)
            .ThenBy(i => i.CreatedAt)
            .Select(i => new ShoppingItemResponse(i.Id, i.Name, i.Quantity, i.Unit, i.Category, i.Checked, i.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        CreateShoppingItemRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var category = req.Category is { } c && ProductCategories.IsValid(c) ? c : ProductCategories.Other;

        var entity = new ShoppingItem
        {
            UserId = uid,
            FridgeId = resolved.Value.FridgeId,
            Name = req.Name.Trim(),
            Quantity = req.Quantity,
            Unit = req.Unit,
            Category = category
        };

        db.ShoppingItems.Add(entity);
        await db.SaveChangesAsync(ct);

        var resp = new ShoppingItemResponse(entity.Id, entity.Name, entity.Quantity, entity.Unit, entity.Category, entity.Checked, entity.CreatedAt);
        return Results.Created($"/shopping/{entity.Id}", resp);
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateShoppingItemRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        if (me.UserId is not int _) return Results.Unauthorized();
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        // Match by id + fridge (not by user) so members of a shared fridge can edit each
        // other's items. Cross-fridge access is blocked by the fridge filter.
        var entity = await db.ShoppingItems
            .FirstOrDefaultAsync(i => i.Id == id && i.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null) return Results.NotFound(new { code = "SHOPPING_ITEM_NOT_FOUND", error = "Shopping item not found" });

        if (req.Name is { } n)
        {
            if (string.IsNullOrWhiteSpace(n)) return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Name cannot be empty"]
            });
            entity.Name = n.Trim();
        }
        if (req.Quantity is { } q) entity.Quantity = q;
        if (req.Unit is not null) entity.Unit = string.IsNullOrWhiteSpace(req.Unit) ? null : req.Unit.Trim();
        if (req.Category is { } cat)
        {
            if (!ProductCategories.IsValid(cat)) return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["category"] = ["Unknown category"]
            });
            entity.Category = cat;
        }
        if (req.Checked is { } c) entity.Checked = c;

        await db.SaveChangesAsync(ct);
        var resp = new ShoppingItemResponse(entity.Id, entity.Name, entity.Quantity, entity.Unit, entity.Category, entity.Checked, entity.CreatedAt);
        return Results.Ok(resp);
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        if (me.UserId is not int _) return Results.Unauthorized();
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var affected = await db.ShoppingItems
            .Where(i => i.Id == id && i.FridgeId == resolved.Value.FridgeId)
            .ExecuteDeleteAsync(ct);

        return affected == 0
            ? Results.NotFound(new { code = "SHOPPING_ITEM_NOT_FOUND", error = "Shopping item not found" })
            : Results.Ok(new { success = true });
    }

    private static async Task<IResult> PurchaseAsync(
        int id,
        PurchaseShoppingItemRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var item = await db.ShoppingItems
            .FirstOrDefaultAsync(i => i.Id == id && i.FridgeId == resolved.Value.FridgeId, ct);
        if (item is null) return Results.NotFound(new { code = "SHOPPING_ITEM_NOT_FOUND", error = "Shopping item not found" });

        var product = new Product
        {
            Name = item.Name,
            Quantity = item.Quantity is { } q && q > 0 ? q : 1m,
            Unit = string.IsNullOrWhiteSpace(item.Unit) ? "pcs" : item.Unit!,
            Category = item.Category,
            ExpiryDate = req.ExpiryDate,
            OwnerId = uid,
            FridgeId = resolved.Value.FridgeId
        };

        db.Products.Add(product);
        db.ShoppingItems.Remove(item);
        await db.SaveChangesAsync(ct);

        var resp = new ProductResponse(product.Id, product.Name, product.Description, product.Quantity, product.Unit, product.ExpiryDate, product.Category, product.OwnerId, product.CreatedAt);
        return Results.Created($"/products/{product.Id}", resp);
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
