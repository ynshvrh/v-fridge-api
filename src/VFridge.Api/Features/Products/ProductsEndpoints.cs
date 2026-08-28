using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.Products;

public static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        group.MapGet("/", (IProductsService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListProducts")
            .WithSummary("List the active fridge's products")
            .WithDescription("Ordered by expiry date ascending. Uses the X-Fridge-Id header to pick a fridge or falls back to the caller's first owned fridge.")
            .Produces<List<ProductResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateProductRequest req, IProductsService service, CancellationToken ct) => service.CreateAsync(req, ct))
            .WithName("CreateProduct")
            .WithSummary("Add a new product to the active fridge")
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/cook", (CookRecipeRequest req, IProductsService service, CancellationToken ct) => service.CookAsync(req, ct))
            .WithName("CookRecipe")
            .WithSummary("Cook a recipe: deduct raw ingredients from fridge and add prepared meal container")
            .Produces<CookRecipeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/{id:int}/consume", (int id, ConsumeProductRequest req, IProductsService service, CancellationToken ct) => service.ConsumeAsync(id, req, ct))
            .WithName("ConsumeProduct")
            .WithSummary("Consume a portion of a product/prepared meal and automatically log to nutrition diary")
            .Produces<ConsumeProductResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/{id:int}/eat", (int id, ConsumeProductRequest req, IProductsService service, CancellationToken ct) => service.ConsumeAsync(id, req, ct))
            .WithName("EatProduct")
            .WithSummary("Alias for consume product/prepared meal")
            .Produces<ConsumeProductResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}", (int id, UpdateProductRequest req, IProductsService service, CancellationToken ct) => service.UpdateAsync(id, req, ct))
            .WithName("UpdateProduct")
            .WithSummary("Patch a product in the active fridge")
            .Produces<ProductResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", (int id, IProductsService service, CancellationToken ct) => service.DeleteAsync(id, ct))
            .WithName("DeleteProduct")
            .WithSummary("Delete a product in the active fridge")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/", (IProductsService service, CancellationToken ct) => service.DeleteAllAsync(ct))
            .WithName("DeleteAllProducts")
            .WithSummary("Empty the active fridge (Owner only)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<ApiError>(StatusCodes.Status403Forbidden);

        return app;
    }
}
