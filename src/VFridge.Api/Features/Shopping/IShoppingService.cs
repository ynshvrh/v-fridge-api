using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.Shopping;

public interface IShoppingService
{
    Task<IResult> ListAsync(CancellationToken ct);
    Task<IResult> CreateAsync(CreateShoppingItemRequest req, CancellationToken ct);
    Task<IResult> UpdateAsync(int id, UpdateShoppingItemRequest req, CancellationToken ct);
    Task<IResult> DeleteAsync(int id, CancellationToken ct);
    Task<IResult> PurchaseAsync(int id, PurchaseShoppingItemRequest req, CancellationToken ct);
}
