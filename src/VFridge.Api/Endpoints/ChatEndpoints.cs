using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class ChatEndpoints
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/chat").WithTags("Chat");

        group.MapGet("/", GetHistoryAsync);
        group.MapPost("/", SendAsync).RequireRateLimiting("chat");
        group.MapDelete("/", ClearAsync);

        return app;
    }

    private static async Task<IResult> GetHistoryAsync(VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var since = DateTime.UtcNow - HistoryWindow;
        var items = await db.Chats
            .Where(c => c.UserId == uid && c.CreatedAt >= since)
            .OrderBy(c => c.CreatedAt)
            .Take(20)
            .Select(c => new ChatMessageResponse(c.Id, c.Role, c.Content, c.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> SendAsync(
        SendChatRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        IAiChatService ai,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Content))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = ["Повідомлення не може бути порожнім"]
            });
        }

        var since = DateTime.UtcNow - HistoryWindow;

        var inventory = await db.Products
            .Where(p => p.OwnerId == uid)
            .Select(p => p.Name + " (" + p.Quantity + " " + p.Unit + ")")
            .ToListAsync(ct);

        var inventoryStr = inventory.Count > 0
            ? string.Join(", ", inventory)
            : "Холодильник порожній";

        var history = await db.Chats
            .Where(c => c.UserId == uid && c.CreatedAt >= since)
            .OrderBy(c => c.CreatedAt)
            .Take(6)
            .Select(c => new { c.Role, c.Content })
            .ToListAsync(ct);

        string? aiText;
        try
        {
            aiText = await ai.GenerateReplyAsync(
                history.Select(h => (h.Role, h.Content)).ToList(),
                inventoryStr,
                req.Content,
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI call failed");
            return Results.Problem(
                title: "⚠️ Сталася внутрішня помилка сервісу.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Prune messages older than the history window so storage doesn't grow.
        await db.Chats
            .Where(c => c.UserId == uid && c.CreatedAt < since)
            .ExecuteDeleteAsync(ct);

        db.Chats.Add(new Chat { UserId = uid, Role = "user", Content = req.Content });
        var assistantMsg = new Chat
        {
            UserId = uid,
            Role = "assistant",
            Content = string.IsNullOrWhiteSpace(aiText) ? "Вибачте, я не зміг сформувати відповідь." : aiText
        };
        db.Chats.Add(assistantMsg);

        await db.SaveChangesAsync(ct);

        return Results.Ok(new ChatMessageResponse(assistantMsg.Id, assistantMsg.Role, assistantMsg.Content, assistantMsg.CreatedAt));
    }

    private static async Task<IResult> ClearAsync(VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var deleted = await db.Chats.Where(c => c.UserId == uid).ExecuteDeleteAsync(ct);
        return Results.Ok(new { success = true, deleted });
    }
}
