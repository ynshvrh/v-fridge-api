using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record ProductResponse(
    int Id,
    string Name,
    string? Description,
    decimal Quantity,
    string Unit,
    DateOnly? ExpiryDate,
    int OwnerId,
    DateTime? CreatedAt);

public sealed record CreateProductRequest(
    [Required, MinLength(2, ErrorMessage = "Name is too short")]
    string Name,
    string? Description,
    [Range(0.01, 1_000_000, ErrorMessage = "Quantity must be greater than 0")]
    decimal Quantity,
    [Required] string Unit,
    DateOnly? ExpiryDate);

public sealed record UpdateProductRequest(
    string? Name,
    string? Description,
    decimal? Quantity,
    string? Unit,
    DateOnly? ExpiryDate);
