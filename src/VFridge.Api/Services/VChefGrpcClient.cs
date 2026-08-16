using Grpc.Core;
using VFridge.Api.Contracts;
using VFridge.Api.Protos.V1;

namespace VFridge.Api.Services;

/// <summary>
/// High-performance gRPC client implementation for communicating with V-Chef microservice.
/// </summary>
public sealed class VChefGrpcClient(
    ChefService.ChefServiceClient grpcClient,
    IConfiguration configuration,
    ILogger<VChefGrpcClient> logger) : IVChefClient
{
    private Metadata CreateMetadata()
    {
        var metadata = new Metadata();
        var token = configuration["VChef:InternalToken"] ?? configuration["VCHEF_INTERNAL_TOKEN"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            metadata.Add("x-internal-token", token);
        }
        return metadata;
    }

    public async Task<VChefRecipeResponse?> GenerateRecipeAsync(VChefGenerateRecipeRequest request, CancellationToken ct = default)
    {
        try
        {
            var grpcRequest = new GenerateRecipeRequest
            {
                MealType = request.MealType ?? "",
                DietaryCategory = request.DietaryCategory ?? "",
                MaxPrepTimeMins = request.MaxPrepTimeMins ?? 0,
                TargetCalories = request.TargetCalories ?? 0
            };
            if (request.Ingredients != null)
            {
                grpcRequest.Ingredients.AddRange(request.Ingredients);
            }

            var response = await grpcClient.GenerateRecipeAsync(
                grpcRequest,
                headers: CreateMetadata(),
                cancellationToken: ct);

            var ingredients = response.Ingredients.Select(i => new VChefIngredient(
                i.Name,
                (decimal)i.Quantity,
                i.Unit,
                i.InFridge
            )).ToList();

            var steps = response.Steps.ToList();
            var generatedAt = DateTime.TryParse(response.GeneratedAt, out var dt) ? dt : DateTime.UtcNow;

            return new VChefRecipeResponse(
                response.Title,
                response.Description,
                response.PrepTimeMins,
                response.CookTimeMins,
                response.Servings,
                response.Calories,
                (decimal)response.ProteinGrams,
                (decimal)response.FatGrams,
                (decimal)response.CarbsGrams,
                ingredients,
                steps,
                generatedAt
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate recipe via V-Chef gRPC client");
            return null;
        }
    }

    public async Task PingHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await grpcClient.HealthCheckAsync(
                new HealthCheckRequest(),
                headers: CreateMetadata(),
                cancellationToken: ct);

            logger.LogInformation("V-Chef gRPC health check status: {Status}", response.Status);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "V-Chef gRPC background health check encountered error");
        }
    }
}
