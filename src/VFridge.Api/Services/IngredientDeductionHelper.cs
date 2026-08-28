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
    // Regex matching leading quantity and optional unit, e.g. "500g flour", "2.5 pcs eggs", "200 g sugar"
    private static readonly Regex LeadingQtyRegex = new(
        @"^\s*(\d+(?:[\.,]\d+)?)\s*([a-zA-Zа-яА-ЯіїєІЇЄ]+)?\s+(.+)$",
        RegexOptions.Compiled);

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

        if (qty is null)
        {
            var match = LeadingQtyRegex.Match(trimmedName);
            if (match.Success)
            {
                if (decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var extractedQty))
                {
                    qty = extractedQty;
                    var extractedUnit = match.Groups[2].Value;
                    if (!string.IsNullOrWhiteSpace(extractedUnit))
                    {
                        unit ??= extractedUnit;
                    }
                    trimmedName = match.Groups[3].Value.Trim();
                }
            }
        }

        return new ParsedIngredient(rawName, trimmedName, qty, unit);
    }

    /// <summary>
    /// Computes the missing quantity of an ingredient after deducting what is already in the fridge and shopping list.
    /// Returns null if the item is fully covered by the fridge/shopping list.
    /// Returns missing quantity (or ingredient quantity if no numeric fridge qty available) if item is needed.
    /// </summary>
    public static (bool IsCovered, decimal? MissingQuantity, string? Unit) CalculateMissing(
        ParsedIngredient ingredient,
        IReadOnlyList<Product> fridgeProducts,
        IReadOnlyList<ShoppingItem> existingShoppingItems)
    {
        var targetName = ingredient.CleanName.ToLowerInvariant();

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
                fridgeQtySum += q;
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
                shoppingQtySum += q;
            }
        }

        var totalAvailable = fridgeQtySum + shoppingQtySum;

        // Case A: Ingredient specifies a numeric quantity (e.g. 500g, 2 pcs)
        if (ingredient.Quantity is { } neededQty && neededQty > 0)
        {
            if (totalAvailable >= neededQty)
            {
                // Fully covered
                return (true, null, ingredient.Unit);
            }

            // If a matching item is already in fridge or shopping list without explicit numeric qty (or shopping list item exists),
            // consider it covered to avoid adding duplicates to shopping list.
            if ((hasFridgeProduct && !hasFridgeQty) || (hasShoppingProduct && shoppingQtySum == 0))
            {
                return (true, null, ingredient.Unit);
            }

            var missing = neededQty - totalAvailable;

            // If we have some quantity in fridge/shopping list, but not enough
            return (missing <= 0, missing > 0 ? missing : null, ingredient.Unit);
        }

        // Case B: Ingredient does NOT specify numeric quantity (e.g. "Milk", "Salt")
        // If we already have this product in fridge or shopping list, consider it covered
        if (hasFridgeProduct || hasShoppingProduct)
        {
            return (true, null, ingredient.Unit);
        }

        // Otherwise, not covered
        return (false, null, ingredient.Unit);
    }

    public static bool IsNameMatch(string sourceName, string targetName)
    {
        var src = sourceName.Trim().ToLowerInvariant();
        var tgt = targetName.Trim().ToLowerInvariant();

        if (src == tgt) return true;
        if (src.Contains(tgt) || tgt.Contains(src)) return true;

        return false;
    }

    public static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return string.Empty;
        var u = unit.Trim().ToLowerInvariant().TrimEnd('.');
        return u switch
        {
            "кг" or "kg" or "кілограм" or "килограмм" or "килограм" => "kg",
            "г" or "g" or "грам" or "грамм" or "гр" => "g",
            "л" or "l" or "літр" or "литр" => "l",
            "мл" or "ml" or "мілілітр" or "миллилитр" => "ml",
            "шт" or "pcs" or "штук" or "pc" or "piece" or "pieces" => "pcs",
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
