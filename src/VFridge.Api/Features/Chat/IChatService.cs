using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.Chat;

public interface IChatService
{
    Task<IResult> GetHistoryAsync(CancellationToken ct);
    Task<IResult> SendAsync(SendChatRequest req, CancellationToken ct);
    Task<IResult> ClearAsync(CancellationToken ct);
}
