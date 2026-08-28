using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace VFridge.Api.Features.SavedRecipes;

public static class SavedRecipeEndpoints
{
    public static IEndpointRouteBuilder MapSavedRecipeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/saved-recipes").WithTags("SavedRecipes");

        group.MapGet("/", (ISavedRecipeService service, CancellationToken ct) => service.GetSavedRecipesAsync(ct))
            .WithName("GetSavedRecipes")
            .WithSummary("List all saved recipes for the current user")
            .Produces<List<SavedRecipeResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (SaveRecipeRequest req, ISavedRecipeService service, CancellationToken ct) => service.SaveRecipeAsync(req, ct))
            .WithName("SaveRecipe")
            .WithSummary("Save a recipe to user's favorites")
            .Produces<SavedRecipeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:int}", (int id, ISavedRecipeService service, CancellationToken ct) => service.DeleteSavedRecipeAsync(id, ct))
            .WithName("DeleteSavedRecipe")
            .WithSummary("Delete a saved recipe by ID")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
