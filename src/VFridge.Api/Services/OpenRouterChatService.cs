using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;

namespace VFridge.Api.Services;

public sealed class OpenRouterChatService(
    HttpClient http,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterChatService> logger) : IAiChatService
{
    private const string SystemPrompt =
        "You are the V-Fridge chef. Your job is to suggest a quick, tasty, and realistic recipe " +
        "in English based on the items the user currently has. " +
        "Be concise: a short ingredient list and a few cooking steps. If the fridge is empty, " +
        "suggest a simple minimal set of items to buy.";

    private readonly OpenRouterOptions _opts = options.Value;

    public async Task<string?> GenerateReplyAsync(
        IReadOnlyList<(string Role, string Content)> history,
        string fridgeInventory,
        string userPrompt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
        {
            logger.LogWarning("OpenRouter ApiKey is not configured");
            return "The AI chef is temporarily unavailable (no API key configured).";
        }

        var messages = new List<ChatMessage>
        {
            new("system", SystemPrompt),
            new("system", $"Current inventory: {fridgeInventory}")
        };

        foreach (var (role, content) in history)
        {
            var normalisedRole = role switch
            {
                "assistant" or "model" => "assistant",
                _ => "user"
            };
            messages.Add(new ChatMessage(normalisedRole, content));
        }

        messages.Add(new ChatMessage("user", userPrompt));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new ChatCompletionRequest(_opts.Model, messages, _opts.MaxTokens))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        if (!string.IsNullOrWhiteSpace(_opts.Referer)) request.Headers.TryAddWithoutValidation("HTTP-Referer", _opts.Referer);
        if (!string.IsNullOrWhiteSpace(_opts.Title)) request.Headers.TryAddWithoutValidation("X-Title", _opts.Title);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("OpenRouter call failed: {Status} — {Body}", (int)response.StatusCode, body);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
        return payload?.Choices?.FirstOrDefault()?.Message?.Content;
    }

    private sealed record ChatMessage([property: JsonPropertyName("role")] string Role,
                                      [property: JsonPropertyName("content")] string Content);

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
