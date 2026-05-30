namespace VFridge.Api.Configuration;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "google/gemini-2.5-flash";

    /// <summary>
    /// Cap on tokens OpenRouter is allowed to generate per call. OpenRouter reserves
    /// credits up-front against the requested ceiling, so a free / low-balance account
    /// gets a 402 ("can only afford N tokens") when this exceeds the remaining budget.
    /// 2048 stays within the free tier. To keep meal recipes within this ceiling the
    /// planner generates a light plan (names + ingredients) and fetches each recipe
    /// lazily, one meal at a time — see OpenRouterMealPlannerService.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Optional HTTP-Referer header value — OpenRouter uses it for app attribution.</summary>
    public string? Referer { get; set; } = "https://v-fridge.app";

    /// <summary>Optional X-Title header value — appears in OpenRouter analytics.</summary>
    public string? Title { get; set; } = "V-Fridge";
}
