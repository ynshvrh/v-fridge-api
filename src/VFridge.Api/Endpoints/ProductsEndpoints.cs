using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;

namespace VFridge.Api.Endpoints;

public static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        group.MapGet("/", ListAsync)
            .WithName("ListProducts")
            .WithSummary("List the caller's products")
            .WithDescription("Ordered by expiry date ascending. Owned by the bearer-token user.")
            .Produces<List<ProductResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .WithName("CreateProduct")
            .WithSummary("Add a new product to the caller's fridge")
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}", UpdateAsync)
            .WithName("UpdateProduct")
            .WithSummary("Patch one of the caller's products")
            .WithDescription("Only fields supplied in the body are updated; the rest stay as they are.")
            .Produces<ProductResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteProduct")
            .WithSummary("Delete one of the caller's products")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/", DeleteAllAsync)
            .WithName("DeleteAllProducts")
            .WithSummary("Empty the caller's fridge")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> ListAsync(VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var items = await db.Products
            .Where(p => p.OwnerId == uid)
            .OrderBy(p => p.ExpiryDate)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Description, p.Quantity, p.Unit, p.ExpiryDate, p.Category, p.OwnerId, p.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        CreateProductRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

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
            OwnerId = uid
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
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var entity = await db.Products.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == uid, ct);
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

    private static async Task<IResult> DeleteAsync(int id, VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var affected = await db.Products
            .Where(p => p.Id == id && p.OwnerId == uid)
            .ExecuteDeleteAsync(ct);

        return affected == 0
            ? Results.NotFound(new { code = "PRODUCT_NOT_FOUND", error = "Product not found" })
            : Results.Ok(new { success = true });
    }

    private static async Task<IResult> DeleteAllAsync(VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var deleted = await db.Products.Where(p => p.OwnerId == uid).ExecuteDeleteAsync(ct);
        return Results.Ok(new { success = true, deleted });
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
