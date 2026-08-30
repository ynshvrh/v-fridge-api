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
                                // E.g. "1 морква" where "морква" was caught in group 2
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

    private static string CleanNoise(string name)
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

    /// <summary>
    /// Computes the missing quantity of an ingredient after deducting what is already in the fridge and shopping list.
    /// </summary>
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

    public static bool IsNameMatch(string sourceName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
            return false;

        var src = sourceName.Trim().ToLowerInvariant();
        var tgt = targetName.Trim().ToLowerInvariant();

        if (src == tgt) return true;
        if (src.Contains(tgt) || tgt.Contains(src)) return true;

        // Match by Ukrainian word stem (min 3 chars)
        var srcWords = src.Split([' ', ',', '.', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        var tgtWords = tgt.Split([' ', ',', '.', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var sw in srcWords)
        {
            if (sw.Length < 3) continue;
            var stemS = sw.Length >= 3 ? sw[..3] : sw;

            foreach (var tw in tgtWords)
            {
                if (tw.Length < 3) continue;
                var stemT = tw.Length >= 3 ? tw[..3] : tw;

                if (stemS == stemT) return true;
            }
        }

        return false;
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

    public static bool IsOptionalSeasoningOrSauce(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var lower = name.Trim().ToLowerInvariant();
        string[] optionalKeywords = [
            "сіль", "соль", "salt",
            "перець", "перец", "pepper",
            "олія", "масло", "oil",
            "спеції", "специи", "spice", "spices", "seasoning",
            "соус", "sauce", "соєвий соус", "майонез", "кетчуп",
            "цукор", "сахар", "sugar",
            "зелень", "петрушка", "кріп", "укроп", "parsley", "dill",
            "лавровий лист", "лавровый лист", "bay leaf"
        ];
        return optionalKeywords.Any(k => lower.Contains(k));
    }
}
