namespace VFridge.Api.Contracts;

/// <summary>
/// Cuisine slugs the API officially supports for chef steering. Keep this in sync with
/// the CHECK constraint in <c>008_user_cuisine_preference.sql</c>. The catalog is
/// intentionally compact (~12); the LLM extrapolates well within these buckets.
/// </summary>
public static class SupportedCuisines
{
    /// <summary>Neutral default — chef does not bias toward any region.</summary>
    public const string Any = "any";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ukrainian",
        "georgian",
        "italian",
        "french",
        "mexican",
        "middle-eastern",
        "indian",
        "chinese",
        "japanese",
        "thai",
        "american",
        Any,
    };

    public static bool IsSupported(string? slug) =>
        !string.IsNullOrWhiteSpace(slug) && All.Contains(slug);

    public static string Normalize(string? slug) =>
        IsSupported(slug) ? slug!.ToLowerInvariant() : Any;
}
