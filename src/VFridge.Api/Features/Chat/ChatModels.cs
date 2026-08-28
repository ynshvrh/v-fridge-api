using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Features.Chat;

public sealed record ChatMessageResponse(
    long Id,
    string Role,
    string Content,
    DateTime? CreatedAt);

public sealed record SendChatRequest(
    [property: Required(ErrorMessage = "Content is required")]
    string Content);
