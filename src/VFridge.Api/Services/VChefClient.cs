using System.Net.Http.Json;
using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public interface IVChefClient
{
    Task<VChefRecipeResponse?> GenerateRecipeAsync(VChefGenerateRecipeRequest request, CancellationToken ct = default);
    Task PingHealthAsync(CancellationToken ct = default);
}

public sealed class VChefClient(HttpClient http, ILogger<VChefClient> logger) : IVChefClient
{
    public async Task<VChefRecipeResponse?> GenerateRecipeAsync(VChefGenerateRecipeRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/v1/recipes/generate", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errContent = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("V-Chef microservice returned status {Status}: {Error}", response.StatusCode, errContent);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<VChefRecipeResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to communicate with V-Chef microservice at {BaseAddress}", http.BaseAddress);
            return null;
        }
    }

    public async Task PingHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await http.GetAsync("/health", ct);
            logger.LogInformation("V-Chef warmup health ping response: {StatusCode}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "V-Chef background warmup ping encountered error (service spinning up)");
        }
    }
}
