using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.Shopping;

public static class ShoppingEndpoints
{
    public static IEndpointRouteBuilder MapShoppingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shopping").WithTags("Shopping");

        group.MapGet("/", (IShoppingService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListShoppingItems")
            .WithSummary("List the caller's shopping items")
            .WithDescription("Ordered with unchecked items first, then by created_at ascending.")
            .Produces<List<ShoppingItemResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateShoppingItemRequest req, IShoppingService service, CancellationToken ct) => service.CreateAsync(req, ct))
            .WithName("CreateShoppingItem")
            .WithSummary("Add an item to the shopping list")
            .Produces<ShoppingItemResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}", (int id, UpdateShoppingItemRequest req, IShoppingService service, CancellationToken ct) => service.UpdateAsync(id, req, ct))
            .WithName("UpdateShoppingItem")
            .WithSummary("Patch one of the caller's shopping items")
            .Produces<ShoppingItemResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", (int id, IShoppingService service, CancellationToken ct) => service.DeleteAsync(id, ct))
            .WithName("DeleteShoppingItem")
            .WithSummary("Delete one of the caller's shopping items")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:int}/purchase", (int id, PurchaseShoppingItemRequest req, IShoppingService service, CancellationToken ct) => service.PurchaseAsync(id, req, ct))
            .WithName("PurchaseShoppingItem")
            .WithSummary("Mark a shopping item as purchased and move it into the fridge")
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
