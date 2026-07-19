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
        "ukrainian" => Bias("Ukrainian", "red borscht, green borscht, varenyky with potato/cabbage/mushrooms/cherries, deruny, holubtsi, syrniki, linyvi varenyky, nalisnyky, banosh with bryndza, kruchenyky, poltavski halushky, kotleta po-kyivsky, salat olivier, vinegret, domashnya pechenya, kulish, bograch, kapustnyak, shpundra, hrechanyky"),
        "georgian"  => Bias("Georgian", "khachapuri, khinkali, lobio, mtsvadi, ajapsandali, badrijani, chashushuli, shkmeruli, satsivi, chikhirtma, lobiani"),
        "italian"   => Bias("Italian", "pasta carbonara/bolognese/pesto, risotto, polenta, frittata, minestrone, panzanella, gnocchi, lasagne, caprese, foccacia, bruschetta"),
        "french"    => Bias("French", "omelette, ratatouille, soupe à l'oignon, quiche, blanquette, coq au vin, beef bourguignon, croque monsieur, bouillabaisse, cassoulet, gratin dauphinois"),
        "mexican"   => Bias("Mexican", "tacos, quesadillas, fajitas, chilaquiles, sopes, tinga, enchiladas, burritos, tostadas, guacamole, pozole, tamales"),
        "middle-eastern" => Bias("Middle Eastern", "hummus, shakshuka, tabbouleh, kebabs, falafel, fattoush, baba ganoush, shawarma, mujadara, kofta, manakish"),
        "indian"    => Bias("Indian", "curries, dal, biryani, chana masala, samosas, paratha, butter chicken, aloo gobi, palak paneer, tikka masala, korma"),
        "chinese"   => Bias("Chinese", "stir-fries, fried rice, mapo tofu, dumplings, noodle soups, kung pao chicken, chow mein, hot pot, scallion pancakes, wontons"),
        "japanese"  => Bias("Japanese", "donburi, ramen, miso soup, onigiri, teriyaki, yakisoba, tempura, udon, gyudon, katsu curry, tamagoyaki"),
        "thai"      => Bias("Thai", "pad thai, green curry, tom yum, larb, basil chicken, pad see ew, massaman curry, mango sticky rice, som tum"),
        "american"  => Bias("American", "grilled meats, BBQ, burgers, casseroles, mac and cheese, meatloaf, chili, clam chowder, pot roast, pancakes, waffles"),
        _ => null // "any" or unknown — chef stays neutral
    };

    private static string Bias(string cuisineLabel, string examples) =>
        $"The user prefers {cuisineLabel} cuisine — typically dishes like {examples}. " +
        "Suggest a wide variety of different authentic dishes from that culinary tradition based on what inventory allows. " +
        "Do not repeat the same dish every day. If the inventory does not cover it, adapt or suggest the closest substitute rather than switching cuisines.";

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
