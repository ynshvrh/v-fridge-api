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
    /// Strict gastronomic realism, flavour pairing, and non-repetition rules.
    /// Prevents absurd ingredient collisions (e.g. sweet porridge with poultry) and ensures high culinary variety.
    /// </summary>
    public const string CulinarySanityRules =
        "CRITICAL GASTRONOMIC & REALISM RULES:\n" +
        "1. Real Culinary Authenticity: Every suggested meal must be a well-known, delicious, realistic recipe that real people cook. NEVER invent absurd or bizarre Frankenstein concoctions just to force unrelated fridge items into one dish (e.g., NEVER put chicken/meat into sweet milk porridge or cereal; NEVER mix chocolate or confectionery with fish/meat; NEVER put whole raw bananas into savory borsch or pasta).\n" +
        "2. Flavor Pairing & Logic: If the fridge contains unrelated ingredients (e.g. oats, milk, and chicken breast), create separate sensible meals (e.g., Oatmeal with fruits/honey for breakfast, and Pan-seared chicken with vegetables for lunch). DO NOT blend them together into one dish.\n" +
        "3. Diversity & No Repetitions: Maximize variety across meals. Do not repeat the same main dish within the week. Alternate protein sources (poultry, beef, fish, eggs, cheese, legumes, tofu) and cooking methods (baking, pan-searing, boiling, fresh salads, slow-simmering). A single ingredient must not dominate all 7 days.\n" +
        "4. Meal Appropriateness:\n" +
        "   - Breakfast: Classic morning dishes (e.g., omelets, scrambled eggs, shakshuka, syrniki, pancakes, waffles, toasts, avocado toast, oatmeal/granola with fruits).\n" +
        "   - Lunch: Wholesome, balanced meals (e.g., hearty soups, borsch, grain bowls, pastas, stews, meat/fish with classic sides like rice, potatoes, or buckwheat, and side salads).\n" +
        "   - Dinner: Satisfying, comforting or light dinners (e.g., roasted vegetables, baked fish, grilled meat, stir-fries, warm salads, casseroles).\n" +
        "5. Gap Handling: If a classic dish requires common staple items not in the fridge (like onion, garlic, olive oil, herbs, sour cream), propose the authentic dish and list the missing items in gapItems rather than mutating the dish into an unpalatable meal.";

    /// <summary>
    /// Culinary steering based on the user's cuisine preference. Expects an already-normalised
    /// cuisine code (see <see cref="Contracts.SupportedCuisines.Normalize"/>). Returns null for
    /// "any"/unknown so the model stays neutral. Keep entries short; the model is sensitive to
    /// long prompts.
    /// </summary>
    public static string? CultureContextFor(string cuisine) => cuisine switch
    {
        "ukrainian" => Bias("Ukrainian", "red borscht with garlic pampushky, green sorrel borsch, varenyky with potato/cabbage/mushrooms/cherries, deruny with sour cream and mushrooms, holubtsi in tomato sauce, syrniki, linyvi varenyky, nalisnyky with cottage cheese, banosh with bryndza and cracklings, kruchenyky, poltavski halushky with chicken and mushrooms, kotleta po-kyivsky, shuba, vinegret, domashnya pechenya in pots, kulish, bograch, kapustnyak, shpundra, hrechanyky in mushroom gravy"),
        "georgian"  => Bias("Georgian", "khachapuri imeruli/adjarian, khinkali with meat, lobio in clay pot, mtsvadi, ajapsandali, badrijani nigvzit (eggplant rolls with walnut paste), chashushuli, shkmeruli (garlic chicken), satsivi, chikhirtma soup, lobiani, kubdari"),
        "italian"   => Bias("Italian", "pasta carbonara, ragù alla bolognese, pasta al pesto genovese, saffron risotto alla Milanese, mushroom risotto, polenta with cheese, frittata with herbs, minestrone, panzanella, potato gnocchi with sage butter, classic lasagne al forno, caprese salad, rosemary foccacia, tomato basil bruschetta, chicken piccata"),
        "french"    => Bias("French", "herb omelette, ratatouille provençale, soupe à l'oignon gratinated with gruyère, quiche lorraine, blanquette de veau, coq au vin, boeuf bourguignon, croque monsieur, bouillabaisse, cassoulet, gratin dauphinois, salade niçoise, tarte tatin"),
        "mexican"   => Bias("Mexican", "tacos al pastor/carnitas/tinga, cheesy quesadillas, chicken/beef fajitas, chilaquiles verdes/rojos, sopes, chicken tinga tostadas, enchiladas suizas, burritos, freshly mashed guacamole with pico de gallo, pozole verde/rojo, tamales"),
        "middle-eastern" => Bias("Middle Eastern", "creamy hummus with warm pita, shakshuka with feta, fresh tabbouleh, spiced chicken/lamb kebabs, crispy falafel, fattoush salad with sumac, smoky baba ganoush, shawarma, mujadara (rice and lentils with crispy onions), spiced kofta skewers, za'atar manakish"),
        "indian"    => Bias("Indian", "chicken tikka masala, butter chicken, rich dal makhani, chana masala with ginger, vegetable samosas, garlic naan, warm paratha, aloo gobi, palak paneer with cumin, fragrant chicken biryani, vegetable korma"),
        "chinese"   => Bias("Chinese", "crispy stir-fry chicken with seasonal vegetables, Yangchow egg fried rice, spicy mapo tofu, steamed pork dumplings (jiaozi), Lanzhou noodle soup, kung pao chicken with peanuts, chow mein, Sichuan hot pot, scallion pancakes, wonton soup"),
        "japanese"  => Bias("Japanese", "katsudon / oyakodon donburi, tonkotsu or shoyu ramen, miso soup with tofu and wakame, onigiri with salmon, chicken teriyaki with steamed rice, yakisoba noodles, vegetable tempura, beef udon, gyudon, Japanese curry rice, rolled tamagoyaki"),
        "thai"      => Bias("Thai", "classic pad thai with lime and peanuts, aromatic green curry with chicken and bamboo, spicy and sour tom yum goong, zesty minced chicken larb, holy basil stir-fry (pad krapow), wide noodle pad see ew, massaman curry with potatoes, green papaya salad (som tum)"),
        "american"  => Bias("American", "smoky BBQ pulled pork or ribs, smashed cheeseburgers, macaroni and cheese with crispy crust, skillet meatloaf with glaze, hearty beef chili, creamy New England clam chowder, slow-cooked pot roast with carrots, fluffy buttermilk pancakes with maple syrup"),
        _ => null // "any" or unknown — chef stays neutral
    };

    private static string Bias(string cuisineLabel, string examples) =>
        $"The user prefers {cuisineLabel} cuisine — typically authentic dishes like {examples}. " +
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
        "uk" => "Write all human-readable text — dish names, descriptions, cooking instructions, notes, and ingredient names — in Ukrainian (natural, appetizing, and culturally authentic).",
        _ => null // English is the model's default
    };
}
