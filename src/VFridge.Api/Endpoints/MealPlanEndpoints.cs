using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class MealPlanEndpoints
{
    public static IEndpointRouteBuilder MapMealPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/meal-plan").WithTags("MealPlan").RequireRateLimiting("chat");

        group.MapPost("/", GenerateAsync)
            .WithName("GenerateMealPlan")
            .WithSummary("Generate a 5-meal weekday plan")
            .WithDescription("Builds the prompt from the caller's current inventory and returns the AI's suggestions. Reuses the same chat rate-limit bucket (5 calls / 60 s per user) since both endpoints hit OpenRouter.")
            .Produces<MealPlanResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapPost("/import-gaps", ImportGapsAsync)
            .WithName("ImportMealPlanGaps")
            .WithSummary("Bulk-append the gap items to the shopping list")
            .WithDescription("Takes a list of meal-plan gap items, dedupes against current unchecked shopping items by name (case-insensitive), and creates rows for the new ones.")
            .Produces<ImportGapsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GenerateAsync(
        VFridgeDbContext db,
        ICurrentUser me,
        IMealPlannerService planner,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var inventory = await db.Products
            .Where(p => p.OwnerId == uid)
            .Select(p => new MealPlanInventoryItem(p.Name, p.Quantity, p.Unit, p.Category))
            .ToListAsync(ct);

        var plan = await planner.GenerateAsync(inventory, ct);
        if (plan is null)
        {
            return Results.Problem(
                title: "The meal planner is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(plan);
    }

    public sealed record ImportGapsRequest(IReadOnlyList<MealPlanGapItem> Items);
    public sealed record ImportGapsResponse(int Created, int Skipped);

    private static async Task<IResult> ImportGapsAsync(
        ImportGapsRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        if (req.Items.Count == 0) return Results.Ok(new ImportGapsResponse(0, 0));

        var existing = await db.ShoppingItems
            .Where(i => i.UserId == uid && !i.Checked)
            .Select(i => i.Name.ToLower())
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var created = 0;
        var skipped = 0;
        foreach (var item in req.Items)
        {
            var trimmed = item.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) { skipped++; continue; }
            if (existingSet.Contains(trimmed.ToLower())) { skipped++; continue; }

            var category = ProductCategories.IsValid(item.Category) ? item.Category : ProductCategories.Other;
            decimal? qty = null;
            if (item.Quantity is { } q && decimal.TryParse(q, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                qty = parsed;
            }

            db.ShoppingItems.Add(new ShoppingItem
            {
                UserId = uid,
                Name = trimmed,
                Quantity = qty,
                Unit = item.Unit,
                Category = category
            });
            existingSet.Add(trimmed.ToLower());
            created++;
        }

        if (created > 0) await db.SaveChangesAsync(ct);
        return Results.Ok(new ImportGapsResponse(created, skipped));
    }
}
