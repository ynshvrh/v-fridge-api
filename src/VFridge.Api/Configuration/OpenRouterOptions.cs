namespace VFridge.Api.Configuration;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "openai/gpt-4o-mini";

    /// <summary>Optional HTTP-Referer header value — OpenRouter uses it for app attribution.</summary>
    public string? Referer { get; set; } = "https://v-fridge.app";

    /// <summary>Optional X-Title header value — appears in OpenRouter analytics.</summary>
    public string? Title { get; set; } = "V-Fridge";
}
