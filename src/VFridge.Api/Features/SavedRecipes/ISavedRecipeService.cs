using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.SavedRecipes;

public interface ISavedRecipeService
{
    Task<IResult> GetSavedRecipesAsync(CancellationToken ct);
    Task<IResult> SaveRecipeAsync(SaveRecipeRequest req, CancellationToken ct);
    Task<IResult> DeleteSavedRecipeAsync(int id, CancellationToken ct);
}
