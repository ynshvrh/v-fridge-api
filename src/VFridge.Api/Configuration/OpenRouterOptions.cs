namespace VFridge.Api.Configuration;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "google/gemini-2.5-flash";

    /// <summary>
    /// Cap on tokens OpenRouter is allowed to generate per call. OpenRouter reserves
    /// credits up-front against the requested ceiling, so leaving it open at the model
    /// max (e.g. 16k) makes low-balance accounts fail with HTTP 402 even on tiny
    /// responses. 4096 fits a 5-meal plan that now carries per-meal descriptions and
    /// cooking steps, including in Ukrainian where Cyrillic costs more tokens. If 402s
    /// appear on a low balance, lower this.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Optional HTTP-Referer header value — OpenRouter uses it for app attribution.</summary>
    public string? Referer { get; set; } = "https://v-fridge.app";

    /// <summary>Optional X-Title header value — appears in OpenRouter analytics.</summary>
    public string? Title { get; set; } = "V-Fridge";
}
