using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record ChatMessageResponse(
    int Id,
    string Role,
    string Content,
    DateTime? CreatedAt);

public sealed record SendChatRequest(
    [property: Required, MinLength(1, ErrorMessage = "Message cannot be empty")]
    string Content);
