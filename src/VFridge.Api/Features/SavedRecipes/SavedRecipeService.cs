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

        List<RecipeIngredientDto> structuredList;
        if (req.StructuredIngredients is { Count: > 0 })
        {
            structuredList = req.StructuredIngredients.ToList();
        }
        else if (req.Ingredients is { Count: > 0 })
        {
            structuredList = req.Ingredients.Select(ing =>
            {
                var parsed = IngredientDeductionHelper.Parse(ing);
                var cat = CategoryInferrer.InferCategory(parsed.CleanName);
                return new RecipeIngredientDto(parsed.CleanName, parsed.Quantity, parsed.Unit, cat, false);
            }).ToList();
        }
        else
        {
            structuredList = new List<RecipeIngredientDto>();
        }

        var stepsList = req.Steps ?? Array.Empty<string>();

        var ingredientsJson = JsonSerializer.Serialize(structuredList, JsonOptions);
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
        var steps = JsonSerializer.Deserialize<List<string>>(r.StepsJson, JsonOptions) ?? new List<string>();

        List<RecipeIngredientDto> structured = new();
        List<string> displayIngredients = new();

        if (!string.IsNullOrWhiteSpace(r.IngredientsJson) && r.IngredientsJson.Trim() != "[]")
        {
            try
            {
                using var doc = JsonDocument.Parse(r.IngredientsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.Object)
                        {
                            var dto = JsonSerializer.Deserialize<RecipeIngredientDto>(el.GetRawText(), JsonOptions);
                            if (dto != null)
                            {
                                structured.Add(dto);
                                var unitDisplay = string.IsNullOrWhiteSpace(dto.Unit) ? "" : dto.Unit.Trim();
                                var str = dto.Quantity.HasValue && !string.IsNullOrWhiteSpace(unitDisplay)
                                    ? $"{dto.Quantity.Value} {unitDisplay} {dto.Name}".Trim()
                                    : (dto.Quantity.HasValue ? $"{dto.Quantity.Value} {dto.Name}".Trim() : dto.Name);
                                displayIngredients.Add(str);
                            }
                        }
                        else if (el.ValueKind == JsonValueKind.String)
                        {
                            var raw = el.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(raw))
                            {
                                displayIngredients.Add(raw);
                                var parsed = IngredientDeductionHelper.Parse(raw);
                                var cat = CategoryInferrer.InferCategory(parsed.CleanName);
                                structured.Add(new RecipeIngredientDto(parsed.CleanName, parsed.Quantity, parsed.Unit, cat, false));
                            }
                        }
                    }
                }
            }
            catch
            {
                // fallback if parsing as json fails
            }
        }

        return new SavedRecipeResponse(
            r.Id,
            r.Name,
            r.Description,
            displayIngredients,
            steps,
            r.Calories,
            r.Protein,
            r.Fat,
            r.Carbs,
            r.CreatedAt,
            structured);
    }
}
