using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Features.Fridges;

public sealed record FridgeResponse(
    int Id,
    string Name,
    int OwnerId,
    string Role,
    int MemberCount,
    DateTime? CreatedAt);

public sealed record CreateFridgeRequest(
    [property: Required, MinLength(1, ErrorMessage = "Name is required"), MaxLength(80)]
    string Name);

public sealed record RenameFridgeRequest(
    [property: Required, MinLength(1), MaxLength(80)]
    string Name);

public sealed record InviteFridgeMemberRequest(
    [property: Required, EmailAddress]
    string Email);

public sealed record AcceptInviteRequest(
    [property: Required]
    string Token);

public sealed record InviteResponse(int Id, string Email, DateTime ExpiresAt, DateTime? AcceptedAt);

public sealed record AcceptInviteResponse(int FridgeId, string FridgeName);
