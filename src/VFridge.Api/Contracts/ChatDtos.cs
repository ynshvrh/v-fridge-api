using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record ChatMessageResponse(
    int Id,
    string Role,
    string Content,
    DateTime? CreatedAt);

public sealed record SendChatRequest(
    [Required, MinLength(1, ErrorMessage = "Повідомлення не може бути порожнім")]
    string Content);
