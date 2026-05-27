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
}
