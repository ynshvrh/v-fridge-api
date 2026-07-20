using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;

namespace VFridge.Api.Endpoints;

public static class SavedRecipeEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record SavedRecipeResponse(
        int Id,
        string Name,
        string? Description,
        IReadOnlyList<string> Ingredients,
        IReadOnlyList<string> Steps,
        int Calories,
        decimal Protein,
        decimal Fat,
        decimal Carbs,
        DateTime CreatedAt);

    public sealed record SaveRecipeRequest(
        string Name,
        string? Description,
        IReadOnlyList<string>? Ingredients,
        IReadOnlyList<string>? Steps,
        int Calories,
        decimal Protein,
        decimal Fat,
        decimal Carbs);

    public static IEndpointRouteBuilder MapSavedRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/saved-recipes").WithTags("SavedRecipes");

        group.MapGet("/", GetSavedRecipesAsync)
            .WithName("GetSavedRecipes")
            .WithSummary("List all saved recipes for the current user")
            .Produces<List<SavedRecipeResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", SaveRecipeAsync)
            .WithName("SaveRecipe")
            .WithSummary("Save a recipe to user's favorites")
            .Produces<SavedRecipeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:int}", DeleteSavedRecipeAsync)
            .WithName("DeleteSavedRecipe")
            .WithSummary("Delete a saved recipe by ID")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetSavedRecipesAsync(
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var rows = await db.SavedRecipes
            .AsNoTracking()
            .Where(r => r.UserId == uid)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var response = rows.Select(MapToResponse).ToList();
        return Results.Ok(response);
    }

    private static async Task<IResult> SaveRecipeAsync(
        SaveRecipeRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        FridgeContext fridgeContext,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return Results.BadRequest(new { error = "Recipe name is required." });
        }

        var resolved = await fridgeContext.ResolveAsync(ct);
        var fridgeId = resolved?.FridgeId;

        var nameTrimmed = req.Name.Trim();
        var existing = await db.SavedRecipes
            .FirstOrDefaultAsync(r => r.UserId == uid && r.Name.ToLower() == nameTrimmed.ToLower(), ct);

        var ingredientsList = req.Ingredients ?? Array.Empty<string>();
        var stepsList = req.Steps ?? Array.Empty<string>();

        var ingredientsJson = JsonSerializer.Serialize(ingredientsList, JsonOptions);
        var stepsJson = JsonSerializer.Serialize(stepsList, JsonOptions);
        var now = DateTime.UtcNow;

        if (existing != null)
        {
            existing.Description = req.Description;
            existing.IngredientsJson = ingredientsJson;
            existing.StepsJson = stepsJson;
            existing.Calories = req.Calories;
            existing.Protein = req.Protein;
            existing.Fat = req.Fat;
            existing.Carbs = req.Carbs;
            existing.CreatedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(MapToResponse(existing));
        }

        var newRecord = new SavedRecipeRecord
        {
            UserId = uid,
            FridgeId = fridgeId,
            Name = nameTrimmed,
            Description = req.Description,
            IngredientsJson = ingredientsJson,
            StepsJson = stepsJson,
            Calories = req.Calories,
            Protein = req.Protein,
            Fat = req.Fat,
            Carbs = req.Carbs,
            CreatedAt = now
        };

        db.SavedRecipes.Add(newRecord);
        await db.SaveChangesAsync(ct);

        return Results.Ok(MapToResponse(newRecord));
    }

    private static async Task<IResult> DeleteSavedRecipeAsync(
        int id,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var record = await db.SavedRecipes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == uid, ct);
        if (record is null) return Results.NotFound();

        db.SavedRecipes.Remove(record);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static SavedRecipeResponse MapToResponse(SavedRecipeRecord r)
    {
        var ingredients = JsonSerializer.Deserialize<List<string>>(r.IngredientsJson, JsonOptions) ?? new List<string>();
        var steps = JsonSerializer.Deserialize<List<string>>(r.StepsJson, JsonOptions) ?? new List<string>();

        return new SavedRecipeResponse(
            r.Id,
            r.Name,
            r.Description,
            ingredients,
            steps,
            r.Calories,
            r.Protein,
            r.Fat,
            r.Carbs,
            r.CreatedAt);
    }
}
