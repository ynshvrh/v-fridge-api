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
}
