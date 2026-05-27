namespace VFridge.Api.Contracts;

/// <summary>
/// Language codes the API officially supports for user-facing surfaces — the chef's
/// cultural prompt context and (later) email templates. The DB column has a matching
/// CHECK constraint in <c>007_user_preferred_language.sql</c>; keep the two in sync.
/// </summary>
public static class SupportedLanguages
{
    public const string Default = "en";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "en",
        "uk"
    };

    public static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) && All.Contains(code);

    /// <summary>Normalises any input to a supported language code, falling back to <see cref="Default"/>.</summary>
    public static string Normalize(string? code) =>
        IsSupported(code) ? code!.ToLowerInvariant() : Default;

    /// <summary>
    /// Parses an Accept-Language header (e.g. "uk-UA,uk;q=0.9,en;q=0.8") and returns the first
    /// supported base language code, or null when none match. Regional tags ("uk-UA") collapse
    /// to their base ("uk"). q-values are ignored — header order is treated as priority order
    /// since browsers already sort by it.
    /// </summary>
    public static string? MatchAcceptLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return null;

        foreach (var raw in acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = raw.Split(';', 2)[0].Trim();
            if (entry.Length == 0) continue;
            var baseCode = entry.Split('-', 2)[0].ToLowerInvariant();
            if (All.Contains(baseCode)) return baseCode;
        }
        return null;
    }
}
