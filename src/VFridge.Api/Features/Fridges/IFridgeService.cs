using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.Fridges;

public interface IFridgeService
{
    Task<IResult> ListAsync(CancellationToken ct);
    Task<IResult> CreateAsync(CreateFridgeRequest req, CancellationToken ct);
    Task<IResult> RenameAsync(int id, RenameFridgeRequest req, CancellationToken ct);
    Task<IResult> DeleteAsync(int id, CancellationToken ct);
    Task<IResult> LeaveAsync(int id, CancellationToken ct);
    Task<IResult> CreateInviteAsync(int id, InviteFridgeMemberRequest req, CancellationToken ct);
    Task<IResult> AcceptInviteAsync(AcceptInviteRequest req, CancellationToken ct);
}
