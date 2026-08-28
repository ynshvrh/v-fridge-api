using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.Products;

public interface IProductsService
{
    Task<IResult> ListAsync(CancellationToken ct);
    Task<IResult> CreateAsync(CreateProductRequest req, CancellationToken ct);
    Task<IResult> UpdateAsync(int id, UpdateProductRequest req, CancellationToken ct);
    Task<IResult> CookAsync(CookRecipeRequest req, CancellationToken ct);
    Task<IResult> ConsumeAsync(int id, ConsumeProductRequest req, CancellationToken ct);
    Task<IResult> DeleteAsync(int id, CancellationToken ct);
    Task<IResult> DeleteAllAsync(CancellationToken ct);
}
