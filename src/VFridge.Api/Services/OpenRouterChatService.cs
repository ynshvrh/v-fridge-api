using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;
using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public sealed class OpenRouterChatService(
    HttpClient http,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterChatService> logger) : IAiChatService
{
    private const string SystemPrompt =
        "You are the V-Fridge chef. Your job is to suggest a quick, tasty, and realistic recipe " +
        "based on the items the user currently has. " +
        "Be concise: a short ingredient list and a few cooking steps. If the fridge is empty, " +
        "suggest a simple minimal set of items to buy.";

    private readonly OpenRouterOptions _opts = options.Value;

    public async Task<string?> GenerateReplyAsync(
        IReadOnlyList<(string Role, string Content)> history,
        string fridgeInventory,
        string userPrompt,
        string cuisinePreference,
        string language,
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

        var culture = AiPrompts.CultureContextFor(SupportedCuisines.Normalize(cuisinePreference));
        if (culture is not null) messages.Add(new ChatMessage("system", culture));

        var languageInstruction = AiPrompts.LanguageInstructionFor(SupportedLanguages.Normalize(language));
        if (languageInstruction is not null) messages.Add(new ChatMessage("system", languageInstruction));

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

        // Try each model in the pool until one returns text. A rate-limited (429),
        // out-of-credit (402), erroring or empty model falls through to the next so
        // a free-tier account stays up across several models' independent limits.
        var models = _opts.ResolvedModels();
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var reply = await TrySendAsync(model, messages, ct);
            if (!string.IsNullOrWhiteSpace(reply))
            {
                if (i > 0) logger.LogInformation("OpenRouter chat served by fallback model {Model} (#{Index})", model, i);
                return reply;
            }
            logger.LogWarning("OpenRouter chat model {Model} unavailable, trying next", model);
        }

        logger.LogError("OpenRouter chat: all {Count} models failed", models.Count);
        return null;
    }

    /// <summary>One attempt against a single model. Returns the reply text, or null on any failure.</summary>
    private async Task<string?> TrySendAsync(string model, List<ChatMessage> messages, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new ChatCompletionRequest(model, messages, _opts.MaxTokens))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        if (!string.IsNullOrWhiteSpace(_opts.Referer)) request.Headers.TryAddWithoutValidation("HTTP-Referer", _opts.Referer);
        if (!string.IsNullOrWhiteSpace(_opts.Title)) request.Headers.TryAddWithoutValidation("X-Title", _opts.Title);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "OpenRouter chat transport error on model {Model}", model);
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("OpenRouter chat {Model} failed: {Status} — {Body}", model, (int)response.StatusCode, body);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
            return payload?.Choices?.FirstOrDefault()?.Message?.Content;
        }
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
