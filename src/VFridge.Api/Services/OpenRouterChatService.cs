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
        "Ти шеф-кухар V-Fridge. Твоя задача — дати швидкий, смачний і реалістичний рецепт " +
        "українською мовою на основі продуктів, які зараз є у користувача. " +
        "Будь стислим: короткий список інгредієнтів і кроки приготування. Якщо продуктів немає " +
        "— запропонуй простий мінімальний набір, який треба докупити.";

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
            return "⚠️ AI-кухар тимчасово недоступний (немає API-ключа).";
        }

        var messages = new List<ChatMessage>
        {
            new("system", SystemPrompt),
            new("system", $"Поточний інвентар: {fridgeInventory}")
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
            Content = JsonContent.Create(new ChatCompletionRequest(_opts.Model, messages))
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
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
