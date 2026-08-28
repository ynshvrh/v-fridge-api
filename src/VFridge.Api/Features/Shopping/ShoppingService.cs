using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Features.Shopping;

public class ShoppingService : IShoppingService
{
    private readonly VFridgeDbContext _db;
    private readonly ICurrentUser _me;
    private readonly FridgeContext _fridgeContext;

    public ShoppingService(VFridgeDbContext db, ICurrentUser me, FridgeContext fridgeContext)
    {
        _db = db;
        _me = me;
        _fridgeContext = fridgeContext;
    }

    public async Task<IResult> ListAsync(CancellationToken ct)
    {
        if (_me.UserId is not int _) return Results.Unauthorized();
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var items = await _db.ShoppingItems
            .Where(i => i.FridgeId == resolved.Value.FridgeId)
            .OrderBy(i => i.Checked)
            .ThenBy(i => i.CreatedAt)
            .Select(i => new ShoppingItemResponse(i.Id, i.Name, i.Quantity, i.Unit, i.Category, i.Checked, i.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    public async Task<IResult> CreateAsync(CreateShoppingItemRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var category = req.Category is { } c && ProductCategories.IsValid(c) ? c : ProductCategories.Other;
        var trimmedName = req.Name.Trim();

        // Deduplication: merge into existing unchecked shopping item if present in the same fridge
        var existing = await _db.ShoppingItems
            .FirstOrDefaultAsync(i => i.FridgeId == resolved.Value.FridgeId &&
                                      !i.Checked &&
                                      i.Name.ToLower() == trimmedName.ToLower(), ct);

        if (existing is not null)
        {
            if (req.Quantity.HasValue && req.Quantity > 0)
            {
                if (existing.Quantity.HasValue && existing.Quantity > 0)
                {
                    if (string.Equals(existing.Unit, req.Unit, StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(existing.Unit))
                    {
                        existing.Quantity += req.Quantity.Value;
                        if (string.IsNullOrWhiteSpace(existing.Unit) && !string.IsNullOrWhiteSpace(req.Unit))
                        {
                            existing.Unit = req.Unit;
                        }
                    }
                    else
                    {
                        var converted = IngredientDeductionHelper.ConvertQuantity(req.Quantity.Value, req.Unit ?? "", existing.Unit);
                        existing.Quantity += converted;
                    }
                }
                else
                {
                    existing.Quantity = req.Quantity;
                    existing.Unit = req.Unit;
                }
            }

            if (existing.Category == ProductCategories.Other && category != ProductCategories.Other)
            {
                existing.Category = category;
            }

            await _db.SaveChangesAsync(ct);
            var mergedResp = new ShoppingItemResponse(existing.Id, existing.Name, existing.Quantity, existing.Unit, existing.Category, existing.Checked, existing.CreatedAt);
            return Results.Ok(mergedResp);
        }

        var entity = new ShoppingItem
        {
            UserId = uid,
            FridgeId = resolved.Value.FridgeId,
            Name = trimmedName,
            Quantity = req.Quantity,
            Unit = req.Unit,
            Category = category
        };

        _db.ShoppingItems.Add(entity);
        await _db.SaveChangesAsync(ct);

        var resp = new ShoppingItemResponse(entity.Id, entity.Name, entity.Quantity, entity.Unit, entity.Category, entity.Checked, entity.CreatedAt);
        return Results.Created($"/shopping/{entity.Id}", resp);
    }

    public async Task<IResult> UpdateAsync(int id, UpdateShoppingItemRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int _) return Results.Unauthorized();
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var entity = await _db.ShoppingItems
            .FirstOrDefaultAsync(i => i.Id == id && i.FridgeId == resolved.Value.FridgeId, ct);
        if (entity is null)
            return Results.NotFound(new { code = "SHOPPING_ITEM_NOT_FOUND", error = "Shopping item not found" });

        if (req.Name is { } n)
        {
            if (string.IsNullOrWhiteSpace(n))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name cannot be empty"]
                });
            }
            entity.Name = n.Trim();
        }
        if (req.Quantity is { } q) entity.Quantity = q;
        if (req.Unit is not null) entity.Unit = string.IsNullOrWhiteSpace(req.Unit) ? null : req.Unit.Trim();
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
        if (req.Checked is { } c) entity.Checked = c;

        await _db.SaveChangesAsync(ct);
        var resp = new ShoppingItemResponse(entity.Id, entity.Name, entity.Quantity, entity.Unit, entity.Category, entity.Checked, entity.CreatedAt);
        return Results.Ok(resp);
    }

    public async Task<IResult> DeleteAsync(int id, CancellationToken ct)
    {
        if (_me.UserId is not int _) return Results.Unauthorized();
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var affected = await _db.ShoppingItems
            .Where(i => i.Id == id && i.FridgeId == resolved.Value.FridgeId)
            .ExecuteDeleteAsync(ct);

        return affected == 0
            ? Results.NotFound(new { code = "SHOPPING_ITEM_NOT_FOUND", error = "Shopping item not found" })
            : Results.Ok(new { success = true });
    }

    public async Task<IResult> PurchaseAsync(int id, PurchaseShoppingItemRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var item = await _db.ShoppingItems
            .FirstOrDefaultAsync(i => i.Id == id && i.FridgeId == resolved.Value.FridgeId, ct);
        if (item is null)
            return Results.NotFound(new { code = "SHOPPING_ITEM_NOT_FOUND", error = "Shopping item not found" });

        var quantityToAdd = item.Quantity is { } q && q > 0 ? q : 1m;
        var unitToUse = string.IsNullOrWhiteSpace(item.Unit) ? "pcs" : item.Unit.Trim();

        var existingProduct = await _db.Products
            .FirstOrDefaultAsync(p =>
                p.FridgeId == resolved.Value.FridgeId &&
                p.Name.ToLower() == item.Name.ToLower().Trim() &&
                p.Unit.ToLower() == unitToUse.ToLower() &&
                p.ExpiryDate == req.ExpiryDate, ct);

        Product product;
        if (existingProduct is not null)
        {
            existingProduct.Quantity += quantityToAdd;
            product = existingProduct;
        }
        else
        {
            product = new Product
            {
                Name = item.Name,
                Quantity = quantityToAdd,
                Unit = unitToUse,
                Category = item.Category,
                ExpiryDate = req.ExpiryDate,
                OwnerId = uid,
                FridgeId = resolved.Value.FridgeId
            };
            _db.Products.Add(product);
        }

        _db.ShoppingItems.Remove(item);
        await _db.SaveChangesAsync(ct);

        var resp = new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.Quantity,
            product.Unit,
            product.ExpiryDate,
            product.Category,
            product.OwnerId,
            product.CreatedAt);

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
