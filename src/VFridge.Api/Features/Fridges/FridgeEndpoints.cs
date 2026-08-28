using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.Fridges;

public static class FridgeEndpoints
{
    public static IEndpointRouteBuilder MapFridgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fridges").WithTags("Fridges");

        group.MapGet("/", (IFridgeService service, CancellationToken ct) => service.ListAsync(ct))
            .WithName("ListFridges")
            .WithSummary("List fridges the caller is a member of")
            .Produces<List<FridgeResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateFridgeRequest req, IFridgeService service, CancellationToken ct) => service.CreateAsync(req, ct))
            .WithName("CreateFridge")
            .WithSummary("Create a new fridge the caller will own")
            .Produces<FridgeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}", (int id, RenameFridgeRequest req, IFridgeService service, CancellationToken ct) => service.RenameAsync(id, req, ct))
            .WithName("RenameFridge")
            .WithSummary("Rename an owned fridge")
            .Produces<FridgeResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", (int id, IFridgeService service, CancellationToken ct) => service.DeleteAsync(id, ct))
            .WithName("DeleteFridge")
            .WithSummary("Delete a fridge (owner only). Cascades products / members / invites.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:int}/members/me", (int id, IFridgeService service, CancellationToken ct) => service.LeaveAsync(id, ct))
            .WithName("LeaveFridge")
            .WithSummary("Leave a fridge. Owners cannot leave their own fridge — delete it instead.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/invites", (int id, InviteFridgeMemberRequest req, IFridgeService service, CancellationToken ct) => service.CreateInviteAsync(id, req, ct))
            .WithName("CreateFridgeInvite")
            .WithSummary("Invite someone by email to join an owned fridge")
            .Produces<InviteResponse>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapPost("/accept", (AcceptInviteRequest req, IFridgeService service, CancellationToken ct) => service.AcceptInviteAsync(req, ct))
            .WithName("AcceptFridgeInvite")
            .WithSummary("Accept a fridge invite token (requires a verified account)")
            .Produces<AcceptInviteResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
