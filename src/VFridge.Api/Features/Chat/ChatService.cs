using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;
using ChatEntity = VFridge.Api.Data.Entities.Chat;

namespace VFridge.Api.Features.Chat;

public class ChatService : IChatService
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);

    private readonly VFridgeDbContext _db;
    private readonly ICurrentUser _me;
    private readonly FridgeContext _fridgeContext;
    private readonly IAiChatService _ai;
    private readonly ILogger<ChatService> _logger;

    private sealed record InventoryItem(string Name, decimal Quantity, string Unit, string Category);

    public ChatService(
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        IAiChatService ai,
        ILogger<ChatService> logger)
    {
        _db = db;
        _me = me;
        _fridgeContext = fridgeContext;
        _ai = ai;
        _logger = logger;
    }

    public async Task<IResult> GetHistoryAsync(CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var since = DateTime.UtcNow - HistoryWindow;
        var items = await _db.Chats
            .Where(c => c.UserId == uid && c.CreatedAt >= since)
            .OrderBy(c => c.CreatedAt)
            .Take(20)
            .Select(c => new ChatMessageResponse(c.Id, c.Role, c.Content, c.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    public async Task<IResult> SendAsync(SendChatRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Content))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = ["Message cannot be empty"]
            });
        }

        var resolved = await _fridgeContext.ResolveAsync(ct);
        var since = DateTime.UtcNow - HistoryWindow;

        var inventory = resolved is null
            ? new List<InventoryItem>()
            : await _db.Products
                .Where(p => p.FridgeId == resolved.Value.FridgeId)
                .Select(p => new InventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
                .ToListAsync(ct);

        var inventoryStr = inventory.Count > 0
            ? string.Join(", ", inventory.Select(p =>
                $"{p.Name} [{ProductCategories.Label(p.Category)}] ({p.Quantity} {p.Unit})"))
            : "The fridge is empty";

        var history = await _db.Chats
            .Where(c => c.UserId == uid && c.CreatedAt >= since)
            .OrderBy(c => c.CreatedAt)
            .Take(6)
            .Select(c => new { c.Role, c.Content })
            .ToListAsync(ct);

        var prefs = await _db.Users
            .Where(u => u.Id == uid)
            .Select(u => new { u.CuisinePreference, u.PreferredLanguage, u.DietaryProfile })
            .FirstOrDefaultAsync(ct);

        var cuisinePreference = SupportedCuisines.Normalize(prefs?.CuisinePreference);
        var language = SupportedLanguages.Normalize(prefs?.PreferredLanguage);
        var dietaryProfile = prefs?.DietaryProfile;

        string? aiText;
        try
        {
            aiText = await _ai.GenerateReplyAsync(
                history.Select(h => (h.Role, h.Content)).ToList(),
                inventoryStr,
                req.Content,
                cuisinePreference,
                language,
                dietaryProfile,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI call failed");
            return Results.Json(
                new ApiError("AI_UNAVAILABLE", "The AI chef is temporarily unavailable. Please try again shortly."),
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (string.IsNullOrWhiteSpace(aiText))
        {
            return Results.Json(
                new ApiError("AI_UNAVAILABLE", "The AI chef is temporarily unavailable. Please try again shortly."),
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Prune messages older than 24h
        await _db.Chats
            .Where(c => c.UserId == uid && c.CreatedAt < since)
            .ExecuteDeleteAsync(ct);

        _db.Chats.Add(new ChatEntity { UserId = uid, Role = "user", Content = req.Content });
        var assistantMsg = new ChatEntity
        {
            UserId = uid,
            Role = "assistant",
            Content = aiText
        };
        _db.Chats.Add(assistantMsg);

        await _db.SaveChangesAsync(ct);

        return Results.Ok(new ChatMessageResponse(assistantMsg.Id, assistantMsg.Role, assistantMsg.Content, assistantMsg.CreatedAt));
    }

    public async Task<IResult> ClearAsync(CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var deleted = await _db.Chats.Where(c => c.UserId == uid).ExecuteDeleteAsync(ct);
        return Results.Ok(new { success = true, deleted });
    }
}
