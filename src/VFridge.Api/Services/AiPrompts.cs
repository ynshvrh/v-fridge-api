namespace VFridge.Api.Services;

/// <summary>
/// Shared prompt-context builders used by both the chef chat and the meal planner so the two
/// stay consistent. Culture steers the choice of dishes; language steers the response language.
/// Structured / identifier fields (the meal planner's <c>day</c> and <c>category</c> values)
/// always stay in their English machine codes regardless of the requested language — each
/// service pins that in its own system prompt; this helper only localises human-readable prose.
/// </summary>
public static class AiPrompts
{
    /// <summary>
    /// Culinary steering based on the user's cuisine preference. Expects an already-normalised
    /// cuisine code (see <see cref="Contracts.SupportedCuisines.Normalize"/>). Returns null for
    /// "any"/unknown so the model stays neutral. Keep entries short; the model is sensitive to
    /// long prompts.
    /// </summary>
    public static string? CultureContextFor(string cuisine) => cuisine switch
    {
        "ukrainian" => Bias("Ukrainian", "borscht, varenyky, deruny, holubtsi, syrniki, kotleta po-kyivsky, salat olivier"),
        "georgian"  => Bias("Georgian", "khachapuri, khinkali, lobio, mtsvadi, ajapsandali, badrijani"),
        "italian"   => Bias("Italian", "pasta, risotto, polenta, frittata, minestrone, panzanella"),
        "french"    => Bias("French", "omelette, ratatouille, soupe à l'oignon, quiche, blanquette"),
        "mexican"   => Bias("Mexican", "tacos, quesadillas, fajitas, chilaquiles, sopes, tinga"),
        "middle-eastern" => Bias("Middle Eastern", "hummus, shakshuka, tabbouleh, kebabs, falafel, fattoush"),
        "indian"    => Bias("Indian", "curries, dal, biryani, chana masala, samosas, paratha"),
        "chinese"   => Bias("Chinese", "stir-fries, fried rice, mapo tofu, dumplings, noodle soups"),
        "japanese"  => Bias("Japanese", "donburi, ramen, miso soup, onigiri, teriyaki, yakisoba"),
        "thai"      => Bias("Thai", "pad thai, green curry, tom yum, larb, basil chicken"),
        "american"  => Bias("American", "grilled meats, BBQ, burgers, casseroles, mac and cheese"),
        _ => null // "any" or unknown — chef stays neutral
    };

    private static string Bias(string cuisineLabel, string examples) =>
        $"The user prefers {cuisineLabel} cuisine — typically {examples}. " +
        "Bias your suggestions toward dishes from that culinary tradition when the " +
        "inventory allows it. If the inventory does not cover it, adapt or suggest the " +
        "closest substitute rather than switching cuisines.";

    /// <summary>
    /// Language steering based on the user's preferred language. Expects an already-normalised
    /// language code (see <see cref="Contracts.SupportedLanguages.Normalize"/>). Returns null for
    /// English — the model's default — so we don't spend prompt budget restating it. This only
    /// affects human-readable prose; callers keep their machine codes English separately.
    /// </summary>
    public static string? LanguageInstructionFor(string language) => language switch
    {
        "uk" => "Write all human-readable text — dish names, notes, ingredient names, and any " +
                "free-form reply — in Ukrainian.",
        _ => null // English is the model's default
    };
}
