using System.Globalization;
using System.Text.RegularExpressions;
using VFridge.Api.Contracts;
using VFridge.Api.Data.Entities;

namespace VFridge.Api.Services;

public sealed record ParsedIngredient(
    string RawText,
    string CleanName,
    decimal? Quantity,
    string? Unit);

public static class IngredientDeductionHelper
{
    // Fraction regex: "1/2 лимона", "3/4 склянки"
    private static readonly Regex FractionRegex = new(
        @"^\s*(\d+)\s*/\s*(\d+)\s*([a-zA-Zа-яА-ЯіїєІЇЄ\.\s]*?)\s+(.+)$",
        RegexOptions.Compiled);

    // Range regex: "1-2 зубчики часнику"
    private static readonly Regex RangeRegex = new(
        @"^\s*(\d+(?:[\.,]\d+)?)\s*-\s*(\d+(?:[\.,]\d+)?)\s*([a-zA-Zа-яА-ЯіїєІЇЄ\.\s]*?)\s+(.+)$",
        RegexOptions.Compiled);

    // Leading quantity + optional unit + remainder: "200г борошна", "2 шт моркви", "1 морква", "3 яйця"
    private static readonly Regex LeadingQtyRegex = new(
        @"^\s*(\d+(?:[\.,]\d+)?)\s*([a-zA-Zа-яА-ЯіїєІЇЄ\.]+)?(?:\s+(.+))?$",
        RegexOptions.Compiled);

    // Pinch phrases: "дрібка солі", "щепотка перцю"
    private static readonly Regex PinchRegex = new(
        @"^\s*(дрібка|щепотка|pinch)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Common Ukrainian inflection suffixes to strip when finding stem (minimum stem length: 4 chars)
    private static readonly string[] InflectionSuffixes = [
        "ами", "ями", "ного", "ному", "них", "ній", "ної", "ним", "ний",
        "ний", "ній", "ная", "ное", "ної", "них", "ним", "ною",
        "ою", "ею", "єю", "ом", "ем", "єм", "ів", "ей",
        "на", "не", "ні", "та", "те", "ті",
        "а", "я", "и", "і", "у", "ю", "е", "є", "о"
    ];

    // Canonical culinary synonym dictionary (maps variations to a canonical root)
    private static readonly Dictionary<string, string> CanonicalAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["курка"] = "CANONICAL_CHICKEN",
        ["куряче"] = "CANONICAL_CHICKEN",
        ["куряча"] = "CANONICAL_CHICKEN",
        ["курячий"] = "CANONICAL_CHICKEN",
        ["курятина"] = "CANONICAL_CHICKEN",
        ["курча"] = "CANONICAL_CHICKEN",
        ["chicken"] = "CANONICAL_CHICKEN",

        ["яйце"] = "CANONICAL_EGG",
        ["яйця"] = "CANONICAL_EGG",
        ["яєць"] = "CANONICAL_EGG",
        ["яйцем"] = "CANONICAL_EGG",
        ["яйцями"] = "CANONICAL_EGG",
        ["egg"] = "CANONICAL_EGG",
        ["eggs"] = "CANONICAL_EGG",

        ["помідор"] = "CANONICAL_TOMATO",
        ["помідори"] = "CANONICAL_TOMATO",
        ["помідорів"] = "CANONICAL_TOMATO",
        ["томат"] = "CANONICAL_TOMATO",
        ["томати"] = "CANONICAL_TOMATO",
        ["томатів"] = "CANONICAL_TOMATO",
        ["tomato"] = "CANONICAL_TOMATO",
        ["tomatoes"] = "CANONICAL_TOMATO",

        ["творог"] = "CANONICAL_COTTAGE_CHEESE",
        ["сир кисломолочний"] = "CANONICAL_COTTAGE_CHEESE",
        ["домашній сир"] = "CANONICAL_COTTAGE_CHEESE",
        ["cottage cheese"] = "CANONICAL_COTTAGE_CHEESE",

        ["масло вершкове"] = "CANONICAL_BUTTER",
        ["вершкове масло"] = "CANONICAL_BUTTER",
        ["масло"] = "CANONICAL_BUTTER",
        ["масла"] = "CANONICAL_BUTTER",
        ["маслом"] = "CANONICAL_BUTTER",
        ["butter"] = "CANONICAL_BUTTER",

        ["олія соняшникова"] = "CANONICAL_OIL",
        ["соняшникова олія"] = "CANONICAL_OIL",
        ["оливкова олія"] = "CANONICAL_OIL",
        ["олія"] = "CANONICAL_OIL",
        ["олії"] = "CANONICAL_OIL",
        ["олією"] = "CANONICAL_OIL",
        ["oil"] = "CANONICAL_OIL",

        ["паста"] = "CANONICAL_PASTA",
        ["макарони"] = "CANONICAL_PASTA",
        ["спагеті"] = "CANONICAL_PASTA",
        ["pasta"] = "CANONICAL_PASTA",
        ["spaghetti"] = "CANONICAL_PASTA",

        ["сіль"] = "CANONICAL_SALT",
        ["солі"] = "CANONICAL_SALT",
        ["соль"] = "CANONICAL_SALT",
        ["сіллю"] = "CANONICAL_SALT",
        ["salt"] = "CANONICAL_SALT",

        ["перець"] = "CANONICAL_PEPPER",
        ["перцю"] = "CANONICAL_PEPPER",
        ["перцем"] = "CANONICAL_PEPPER",
        ["pepper"] = "CANONICAL_PEPPER",

        ["часник"] = "CANONICAL_GARLIC",
        ["часнику"] = "CANONICAL_GARLIC",
        ["часником"] = "CANONICAL_GARLIC",
        ["garlic"] = "CANONICAL_GARLIC",

        ["цибуля"] = "CANONICAL_ONION",
        ["цибулі"] = "CANONICAL_ONION",
        ["цибулею"] = "CANONICAL_ONION",
        ["onion"] = "CANONICAL_ONION",

        ["цукор"] = "CANONICAL_SUGAR",
        ["цукру"] = "CANONICAL_SUGAR",
        ["цукром"] = "CANONICAL_SUGAR",
        ["sugar"] = "CANONICAL_SUGAR"
    };

    public static ParsedIngredient Parse(string rawName, string? rawQuantity = null, string? rawUnit = null)
    {
        var trimmedName = rawName.Trim();
        decimal? qty = null;

        if (!string.IsNullOrWhiteSpace(rawQuantity) &&
            decimal.TryParse(rawQuantity.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedQty))
        {
            qty = parsedQty;
        }

        var unit = string.IsNullOrWhiteSpace(rawUnit) ? null : rawUnit.Trim();
        var cleanName = trimmedName;

        if (qty is null)
        {
            var pinchMatch = PinchRegex.Match(trimmedName);
            if (pinchMatch.Success)
            {
                qty = 1;
                unit ??= "дрібка";
                cleanName = pinchMatch.Groups[2].Value;
            }
            else
            {
                var fracMatch = FractionRegex.Match(trimmedName);
                if (fracMatch.Success &&
                    decimal.TryParse(fracMatch.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var num) &&
                    decimal.TryParse(fracMatch.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var denom) && denom > 0)
                {
                    qty = num / denom;
                    var potUnit = fracMatch.Groups[3].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(potUnit) && string.IsNullOrWhiteSpace(unit)) unit = potUnit;
                    cleanName = fracMatch.Groups[4].Value;
                }
                else
                {
                    var rangeMatch = RangeRegex.Match(trimmedName);
                    if (rangeMatch.Success &&
                        decimal.TryParse(rangeMatch.Groups[2].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var upper))
                    {
                        qty = upper;
                        var potUnit = rangeMatch.Groups[3].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(potUnit) && string.IsNullOrWhiteSpace(unit)) unit = potUnit;
                        cleanName = rangeMatch.Groups[4].Value;
                    }
                    else
                    {
                        var qtyMatch = LeadingQtyRegex.Match(trimmedName);
                        if (qtyMatch.Success &&
                            decimal.TryParse(qtyMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var extractedQty))
                        {
                            qty = extractedQty;
                            var potUnit = qtyMatch.Groups[2].Value.Trim();
                            var remainder = qtyMatch.Groups[3].Value.Trim();

                            if (!string.IsNullOrWhiteSpace(remainder))
                            {
                                if (!string.IsNullOrWhiteSpace(potUnit)) unit ??= potUnit;
                                cleanName = remainder;
                            }
                            else if (!string.IsNullOrWhiteSpace(potUnit) && IsLikelyUnit(potUnit))
                            {
                                unit ??= potUnit;
                                cleanName = string.Empty;
                            }
                            else if (!string.IsNullOrWhiteSpace(potUnit) && !IsLikelyUnit(potUnit))
                            {
                                cleanName = potUnit;
                                unit ??= "pcs";
                            }
                        }
                    }
                }
            }
        }

        cleanName = CleanNoise(cleanName);
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            cleanName = CleanNoise(trimmedName);
        }

        return new ParsedIngredient(rawName, cleanName, qty, unit);
    }

    private static bool IsLikelyUnit(string s)
    {
        var lower = s.Trim().TrimEnd('.').ToLowerInvariant();
        return lower is "г" or "g" or "гр" or "грам" or "грамм" or "кг" or "kg" or "мл" or "ml" or "л" or "l"
            or "шт" or "pcs" or "pc" or "ст" or "стл" or "чл" or "ст.л" or "ч.л" or "tbsp" or "tsp"
            or "зубчик" or "зубчики" or "дрібка" or "щепотка";
    }

    public static string CleanNoise(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var n = name.Trim().Trim('-', '–', '—', ':', ',', '.');
        string[] noiseWords = [
            "великої", "великий", "велика", "великі", "великих",
            "середньої", "середній", "середня", "середні", "середніх",
            "маленької", "маленький", "маленька", "маленькі", "маленьких",
            "свіжого", "свіжий", "свіжа", "свіжі", "свіжих",
            "стиглого", "стиглий", "стигла", "стиглі", "стиглих"
        ];

        foreach (var word in noiseWords)
        {
            if (n.StartsWith(word + " ", StringComparison.OrdinalIgnoreCase))
            {
                n = n[(word.Length + 1)..].Trim();
            }
        }

        return n.Trim('-', '–', '—', ':', ',', '.');
    }

    public static string StripEnding(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return string.Empty;
        var w = word.Trim().ToLowerInvariant();

        foreach (var suffix in InflectionSuffixes)
        {
            if (w.EndsWith(suffix) && (w.Length - suffix.Length) >= 4)
            {
                return w[..^suffix.Length];
            }
        }
        return w;
    }

    public static bool IsNameMatch(string sourceName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
            return false;

        var src = sourceName.Trim().ToLowerInvariant();
        var tgt = targetName.Trim().ToLowerInvariant();

        if (src == tgt) return true;

        // 1. Check Canonical Aliases (e.g. "куряче філе" vs "курка", "томати" vs "помідори")
        if (CanonicalAliases.TryGetValue(src, out var srcCanon) &&
            CanonicalAliases.TryGetValue(tgt, out var tgtCanon) &&
            srcCanon == tgtCanon)
        {
            return true;
        }

        // 2. Tokenize with word boundaries
        var srcWords = src.Split([' ', ',', '.', '-', '(', ')', '%', '"', '\''], StringSplitOptions.RemoveEmptyEntries);
        var tgtWords = tgt.Split([' ', ',', '.', '-', '(', ')', '%', '"', '\''], StringSplitOptions.RemoveEmptyEntries);

        // Check if any source word canonical matches target word canonical
        foreach (var sw in srcWords)
        {
            if (CanonicalAliases.TryGetValue(sw, out var swCanon))
            {
                foreach (var tw in tgtWords)
                {
                    if (CanonicalAliases.TryGetValue(tw, out var twCanon) && swCanon == twCanon)
                        return true;
                }
            }
        }

        // 3. Exact word match in multi-word phrase
        foreach (var sw in srcWords)
        {
            foreach (var tw in tgtWords)
            {
                if (sw == tw) return true;

                // 4. Safe Stem Matching (stems MUST be >= 4 characters)
                var stemS = StripEnding(sw);
                var stemT = StripEnding(tw);

                if (stemS.Length >= 4 && stemT.Length >= 4 && stemS == stemT)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static (bool IsCovered, decimal? MissingQuantity, string? Unit) CalculateMissing(
        ParsedIngredient ingredient,
        IReadOnlyList<Product> fridgeProducts,
        IReadOnlyList<ShoppingItem> existingShoppingItems)
    {
        var targetName = ingredient.CleanName;

        // 1. Check Fridge Products
        var matchingProducts = fridgeProducts
            .Where(p => IsNameMatch(p.Name, targetName))
            .ToList();

        decimal fridgeQtySum = 0;
        bool hasFridgeQty = false;
        bool hasFridgeProduct = matchingProducts.Count > 0;

        foreach (var p in matchingProducts)
        {
            if (p.Quantity is { } q && q > 0)
            {
                var converted = ConvertQuantity(q, p.Unit, ingredient.Unit);
                fridgeQtySum += converted;
                hasFridgeQty = true;
            }
        }

        // 2. Check Existing Shopping Items (Unchecked)
        var matchingShopping = existingShoppingItems
            .Where(s => !s.Checked && IsNameMatch(s.Name, targetName))
            .ToList();

        decimal shoppingQtySum = 0;
        bool hasShoppingProduct = matchingShopping.Count > 0;

        foreach (var s in matchingShopping)
        {
            if (s.Quantity is { } q && q > 0)
            {
                var converted = ConvertQuantity(q, s.Unit, ingredient.Unit);
                shoppingQtySum += converted;
            }
        }

        var totalAvailable = fridgeQtySum + shoppingQtySum;

        // Case A: Ingredient specifies a numeric quantity (e.g. 500g, 2 pcs)
        if (ingredient.Quantity is { } neededQty && neededQty > 0)
        {
            if (totalAvailable >= neededQty)
            {
                return (true, null, ingredient.Unit);
            }

            if ((hasFridgeProduct && !hasFridgeQty) || (hasShoppingProduct && shoppingQtySum == 0))
            {
                return (true, null, ingredient.Unit);
            }

            var missing = neededQty - totalAvailable;
            return (missing <= 0, missing > 0 ? missing : null, ingredient.Unit);
        }

        // Case B: Ingredient does NOT specify numeric quantity (e.g. "Milk", "Salt")
        if (hasFridgeProduct || hasShoppingProduct)
        {
            return (true, null, ingredient.Unit);
        }

        return (false, null, ingredient.Unit);
    }

    public static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return string.Empty;
        var u = unit.Trim().ToLowerInvariant().TrimEnd('.');
        return u switch
        {
            "кг" or "kg" or "кілограм" or "кілограмів" or "килограмм" or "килограм" => "kg",
            "г" or "g" or "грам" or "грамів" or "грамм" or "гр" => "g",
            "л" or "l" or "літр" or "літрів" or "литр" => "l",
            "мл" or "ml" or "мілілітр" or "мілілітрів" or "миллилитр" => "ml",
            "шт" or "pcs" or "штук" or "штуки" or "штука" or "pc" or "piece" or "pieces" => "pcs",
            "ст.л" or "ст. л" or "ст л" or "столова ложка" or "столові ложки" or "tbsp" => "ст.л.",
            "ч.л" or "ч. л" or "ч л" or "чайна ложка" or "чайні ложки" or "tsp" => "ч.л.",
            "дрібка" or "щепотка" or "pinch" => "дрібка",
            "зубчик" or "зубчики" or "зубчиків" or "clove" or "cloves" => "зубчик",
            _ => u
        };
    }

    public static decimal ConvertQuantity(decimal quantity, string? fromUnit, string? toUnit)
    {
        var fromNorm = NormalizeUnit(fromUnit);
        var toNorm = NormalizeUnit(toUnit);

        if (fromNorm == toNorm || string.IsNullOrEmpty(fromNorm) || string.IsNullOrEmpty(toNorm))
        {
            return quantity;
        }

        // Weight
        if (fromNorm == "g" && toNorm == "kg") return quantity / 1000m;
        if (fromNorm == "kg" && toNorm == "g") return quantity * 1000m;

        // Volume
        if (fromNorm == "ml" && toNorm == "l") return quantity / 1000m;
        if (fromNorm == "l" && toNorm == "ml") return quantity * 1000m;

        return quantity;
    }

    public static (int Calories, decimal Protein, decimal Fat, decimal Carbs) ParseNutrition(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (0, 0, 0, 0);

        int cal = 0;
        decimal prot = 0, fat = 0, carbs = 0;

        var calMatch = Regex.Match(text, @"(\d+)\s*(?:кКал|ккал|kcal|calories|cal)", RegexOptions.IgnoreCase);
        if (calMatch.Success && int.TryParse(calMatch.Groups[1].Value, out var c))
            cal = c;

        var protMatch = Regex.Match(text, @"(?:Б|Біл(?:ки|ок)?|Protein|Prot)[:\s]+(\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
        if (protMatch.Success && decimal.TryParse(protMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var p))
            prot = p;

        var fatMatch = Regex.Match(text, @"(?:Ж|Жир(?:и)?|Fat)[:\s]+(\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
        if (fatMatch.Success && decimal.TryParse(fatMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var f))
            fat = f;

        var carbMatch = Regex.Match(text, @"(?:В|Вуг(?:леводи)?|Carbs)[:\s]+(\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
        if (carbMatch.Success && decimal.TryParse(carbMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var cb))
            carbs = cb;

        return (cal, prot, fat, carbs);
    }

    public static bool IsOptionalSeasoningOrSauce(ParsedIngredient ingredient)
    {
        var name = ingredient.CleanName;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var lower = name.Trim().ToLowerInvariant();

        // Minor seasonings that are almost always optional in home cooking
        string[] minorSeasonings = [
            "сіль", "соль", "salt",
            "перець", "перец", "pepper", "чорний перець",
            "спеції", "специи", "spice", "spices", "seasoning", "приправа",
            "лавровий лист", "лавровый лист", "bay leaf", "паприка", "куркума", "орегано", "базилік",
            "зелень", "петрушка", "кріп", "укроп", "parsley", "dill"
        ];

        if (minorSeasonings.Any(s => IsNameMatch(s, lower)))
        {
            return true;
        }

        // Conditional seasonings (only optional if small quantities or pinch/spoon)
        string[] conditionalSeasonings = [
            "олія", "oil", "соняшникова олія", "оливкова олія",
            "масло", "butter", "вершкове масло",
            "соус", "sauce", "соєвий соус", "майонез", "кетчуп", "гірчиця",
            "цукор", "sugar", "мед", "часник", "garlic"
        ];

        if (conditionalSeasonings.Any(s => IsNameMatch(s, lower)))
        {
            var unitNorm = NormalizeUnit(ingredient.Unit);
            if (unitNorm is "дрібка" or "ч.л." or "зубчик" or "ст.л.")
                return true;

            if (ingredient.Quantity is { } qty && qty > 0)
            {
                if (unitNorm is "g" or "г" or "ml" or "мл" && qty <= 30)
                    return true;
                if (unitNorm is "pcs" or "шт" && qty <= 1)
                    return true;
                return false;
            }

            return true;
        }

        return false;
    }
}
