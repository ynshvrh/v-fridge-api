namespace VFridge.Api.Configuration;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Single model id. Kept for backward compatibility and as the fallback when
    /// <see cref="Models"/> is empty. Prefer configuring <see cref="Models"/> for failover.
    /// </summary>
    public string Model { get; set; } = "google/gemini-2.5-flash";

    /// <summary>
    /// Ordered pool of model ids tried best-first. When the top model is rate-limited (429),
    /// out of credit (402), errors (5xx), returns nothing, or — for the planner — returns
    /// invalid JSON, the service falls through to the next one. Lets a free-tier account stay
    /// up across several models' independent daily limits without paying. Only put models that
    /// are individually competent here — the chain rescues from outages, not from a weak model's
    /// bad output. Empty → falls back to the single <see cref="Model"/>.
    /// </summary>
    public List<string> Models { get; set; } = new();

    /// <summary>The model pool to try in order: <see cref="Models"/> if set, else just <see cref="Model"/>.</summary>
    public IReadOnlyList<string> ResolvedModels()
    {
        var pool = Models.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        return pool.Count > 0 ? pool : new List<string> { Model };
    }

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
