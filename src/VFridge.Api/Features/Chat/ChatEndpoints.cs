using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.Chat;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chat").WithTags("Chat");

        group.MapGet("/", (IChatService service, CancellationToken ct) => service.GetHistoryAsync(ct))
            .WithName("GetChatHistory")
            .WithSummary("Last 24 h of chat for the caller")
            .WithDescription("Returns up to 20 messages from the last 24 hours, oldest first.")
            .Produces<List<ChatMessageResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (SendChatRequest req, IChatService service, CancellationToken ct) => service.SendAsync(req, ct))
            .RequireRateLimiting("chat")
            .WithName("SendChatMessage")
            .WithSummary("Ask the AI chef")
            .WithDescription("Sends a user message, asks the configured AI provider for a reply, persists both.")
            .Produces<ChatMessageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();

        group.MapDelete("/", (IChatService service, CancellationToken ct) => service.ClearAsync(ct))
            .WithName("ClearChatHistory")
            .WithSummary("Delete the caller's chat history")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
