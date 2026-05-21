using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record ShoppingItemResponse(
    int Id,
    string Name,
    decimal? Quantity,
    string? Unit,
    string Category,
    bool Checked,
    DateTime? CreatedAt);

public sealed record CreateShoppingItemRequest(
    [property: Required, MinLength(1, ErrorMessage = "Name is too short")]
    string Name,
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
