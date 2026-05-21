using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;
using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public sealed class OpenRouterMealPlannerService(
    HttpClient http,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterMealPlannerService> logger) : IMealPlannerService
{
    private const string SystemPrompt =
        "You are V-Fridge's meal planner. Given the user's current inventory, propose exactly 5 distinct " +
        "weekday meals (assign each to one of Monday, Tuesday, Wednesday, Thursday, Friday). For each meal " +
        "list its ingredients. Use what is in the fridge wherever possible; only ask for extra ingredients " +
        "when the meal genuinely needs them. " +
        "Respond with strict JSON matching this schema, no prose: " +
        "{\"meals\":[{\"name\":string,\"day\":string,\"ingredients\":[string],\"note\":string?}]," +
        "\"gapItems\":[{\"name\":string,\"quantity\":string?,\"unit\":string?,\"category\":string}]} " +
        "Category must be one of: dairy, meat-fish, vegetables, fruits, bakery, pantry, snacks, drinks, " +
        "alcohol, sauces, frozen, canned-prepared, other.";

    private readonly OpenRouterOptions _opts = options.Value;

    public async Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured; cannot plan meals");
            return null;
        }

        var inventoryText = inventory.Count == 0
            ? "The fridge is empty."
            : "Current inventory:\n" + string.Join("\n",
                inventory.Select(i => $"- {i.Name} [{ProductCategories.Label(i.Category)}] ({i.Quantity} {i.Unit})"));

        var messages = new List<ChatMessage>
        {
            new("system", SystemPrompt),
            new("user", inventoryText)
        };

        var body = new ChatCompletionRequest(
            _opts.Model,
            messages,
            new ResponseFormat("json_object"));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        if (!string.IsNullOrWhiteSpace(_opts.Referer)) request.Headers.TryAddWithoutValidation("HTTP-Referer", _opts.Referer);
        if (!string.IsNullOrWhiteSpace(_opts.Title)) request.Headers.TryAddWithoutValidation("X-Title", _opts.Title);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("OpenRouter meal-plan call failed: {Status} — {Body}", (int)response.StatusCode, raw);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
        var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var meals = root.TryGetProperty("meals", out var mealsEl) && mealsEl.ValueKind == JsonValueKind.Array
                ? mealsEl.EnumerateArray()
                    .Select(m => new MealPlanMeal(
                        m.GetProperty("name").GetString() ?? "",
                        m.TryGetProperty("day", out var d) ? d.GetString() ?? "" : "",
                        m.TryGetProperty("ingredients", out var ing) && ing.ValueKind == JsonValueKind.Array
                            ? ing.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                            : new List<string>(),
                        m.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null))
                    .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                    .ToList()
                : new List<MealPlanMeal>();

            var gapItems = root.TryGetProperty("gapItems", out var gapsEl) && gapsEl.ValueKind == JsonValueKind.Array
                ? gapsEl.EnumerateArray()
                    .Select(g => new MealPlanGapItem(
                        g.GetProperty("name").GetString() ?? "",
                        g.TryGetProperty("quantity", out var q) && q.ValueKind != JsonValueKind.Null ? q.ToString() : null,
                        g.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null,
                        g.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
                            ? (ProductCategories.IsValid(c.GetString() ?? "") ? c.GetString()! : ProductCategories.Other)
                            : ProductCategories.Other))
                    .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                    .ToList()
                : new List<MealPlanGapItem>();

            return new MealPlanResponse(meals, gapItems);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "OpenRouter meal-plan response was not valid JSON: {Content}", content);
            return null;
        }
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
