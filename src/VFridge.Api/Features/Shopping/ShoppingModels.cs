using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Features.Shopping;

public sealed record ShoppingItemResponse(
    int Id,
    string Name,
    decimal? Quantity,
    string? Unit,
    string Category,
    bool Checked,
    DateTime? CreatedAt);

public sealed record CreateShoppingItemRequest(
    [property: Required, MinLength(1, ErrorMessage = "Name is required")]
    string Name,
    [property: Range(0.01, 1_000_000, ErrorMessage = "Quantity must be greater than 0")]
    decimal? Quantity,
    string? Unit,
    string? Category);

public sealed record UpdateShoppingItemRequest(
    string? Name,
    decimal? Quantity,
    string? Unit,
    string? Category,
    bool? Checked);

public sealed record PurchaseShoppingItemRequest(
    DateOnly? ExpiryDate);
