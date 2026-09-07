namespace VFridge.Api.Contracts;

/// <summary>
/// Unified standard for measurement units across the V-Fridge ecosystem.
/// Provides canonical unit keys, normalization, localized display names (Ukrainian / English),
/// and dimension conversions.
/// </summary>
public static class UnitStandards
{
    // Canonical unit keys
    public const string Piece = "pcs";
    public const string Gram = "g";
    public const string Kilogram = "kg";
    public const string Milliliter = "ml";
    public const string Liter = "l";
    public const string Tablespoon = "tbsp";
    public const string Teaspoon = "tsp";
    public const string Pinch = "pinch";
    public const string Clove = "clove";
    public const string Servings = "servings";
    public const string Pack = "pack";

    public static readonly IReadOnlyList<string> AllCanonical =
    [
        Piece,
        Gram,
        Kilogram,
        Milliliter,
        Liter,
        Tablespoon,
        Teaspoon,
        Pinch,
        Clove,
        Servings,
        Pack
    ];

    private static readonly Dictionary<string, (string Uk, string En)> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [Piece] = ("шт", "pcs"),
        [Gram] = ("г", "g"),
        [Kilogram] = ("кг", "kg"),
        [Milliliter] = ("мл", "ml"),
        [Liter] = ("л", "l"),
        [Tablespoon] = ("ст. л.", "tbsp"),
        [Teaspoon] = ("ч. л.", "tsp"),
        [Pinch] = ("дрібка", "pinch"),
        [Clove] = ("зубчик", "clove"),
        [Servings] = ("порцій", "servings"),
        [Pack] = ("уп", "pack")
    };

    /// <summary>
    /// Normalizes any localized or slang unit string into its canonical key.
    /// </summary>
    public static string Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return string.Empty;
        var u = unit.Trim().ToLowerInvariant().TrimEnd('.');

        return u switch
        {
            "кг" or "kg" or "кілограм" or "кілограмів" or "килограмм" or "килограм" => Kilogram,
            "г" or "g" or "грам" or "грамів" or "грамм" or "гр" => Gram,
            "л" or "l" or "літр" or "літрів" or "литр" => Liter,
            "мл" or "ml" or "мілілітр" or "мілілітрів" or "миллилитр" => Milliliter,
            "шт" or "pcs" or "штук" or "штуки" or "штука" or "pc" or "piece" or "pieces" => Piece,
            "ст.л" or "ст. л" or "ст л" or "столова ложка" or "столові ложки" or "tbsp" or "tablespoon" => Tablespoon,
            "ч.л" or "ч. л" or "ч л" or "чайна ложка" or "чайні ложки" or "tsp" or "teaspoon" => Teaspoon,
            "дрібка" or "щепотка" or "pinch" => Pinch,
            "зубчик" or "зубчики" or "зубчиків" or "clove" or "cloves" => Clove,
            "порція" or "порції" or "порцій" or "порция" or "порций" or "serving" or "servings" => Servings,
            "уп" or "упак" or "упаковка" or "упаковки" or "pack" or "packs" or "pkg" => Pack,
            _ => u
        };
    }

    /// <summary>
    /// Converts a unit (canonical or raw) to the user's localized display unit.
    /// </summary>
    public static string ToDisplayUnit(string? unit, string? language)
    {
        var canonical = Normalize(unit);
        var lang = SupportedLanguages.Normalize(language);

        if (DisplayNames.TryGetValue(canonical, out var names))
        {
            return lang == "uk" ? names.Uk : names.En;
        }

        return unit?.Trim() ?? (lang == "uk" ? "шт" : "pcs");
    }

    /// <summary>
    /// Converts quantity from one unit to another compatible unit (e.g. g -> kg, ml -> l).
    /// </summary>
    public static decimal Convert(decimal quantity, string? fromUnit, string? toUnit)
    {
        var fromNorm = Normalize(fromUnit);
        var toNorm = Normalize(toUnit);

        if (fromNorm == toNorm || string.IsNullOrEmpty(fromNorm) || string.IsNullOrEmpty(toNorm))
        {
            return quantity;
        }

        if (fromNorm == Gram && toNorm == Kilogram) return quantity / 1000m;
        if (fromNorm == Kilogram && toNorm == Gram) return quantity * 1000m;

        if (fromNorm == Milliliter && toNorm == Liter) return quantity / 1000m;
        if (fromNorm == Liter && toNorm == Milliliter) return quantity * 1000m;

        return quantity;
    }

    /// <summary>
    /// Returns true if two units can be merged or compared directly or via scalar conversion.
    /// </summary>
    public static bool AreCompatible(string? unitA, string? unitB)
    {
        var a = Normalize(unitA);
        var b = Normalize(unitB);

        if (a == b) return true;
        if ((a == Gram && b == Kilogram) || (a == Kilogram && b == Gram)) return true;
        if ((a == Milliliter && b == Liter) || (a == Liter && b == Milliliter)) return true;

        return false;
    }
}
