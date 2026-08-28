using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Features.SavedRecipes;

public class SavedRecipeService : ISavedRecipeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VFridgeDbContext _db;
    private readonly ICurrentUser _me;
    private readonly FridgeContext _fridgeContext;

    public SavedRecipeService(VFridgeDbContext db, ICurrentUser me, FridgeContext fridgeContext)
    {
        _db = db;
        _me = me;
        _fridgeContext = fridgeContext;
    }

    public async Task<IResult> GetSavedRecipesAsync(CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var rows = await _db.SavedRecipes
            .AsNoTracking()
            .Where(r => r.UserId == uid)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var response = rows.Select(MapToResponse).ToList();
        return Results.Ok(response);
    }

    public async Task<IResult> SaveRecipeAsync(SaveRecipeRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return Results.BadRequest(new { error = "Recipe name is required." });
        }

        var resolved = await _fridgeContext.ResolveAsync(ct);
        var fridgeId = resolved?.FridgeId;

        var nameTrimmed = req.Name.Trim();
        var existing = await _db.SavedRecipes
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
            await _db.SaveChangesAsync(ct);
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

        _db.SavedRecipes.Add(newRecord);
        await _db.SaveChangesAsync(ct);

        return Results.Ok(MapToResponse(newRecord));
    }

    public async Task<IResult> DeleteSavedRecipeAsync(int id, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var record = await _db.SavedRecipes.FirstOrDefaultAsync(r => r.Id == id && r.UserId == uid, ct);
        if (record is null) return Results.NotFound();

        _db.SavedRecipes.Remove(record);
        await _db.SaveChangesAsync(ct);

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
