using System.Text.RegularExpressions;
using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public static class CategoryInferrer
{
    /// <summary>
    /// Infers the appropriate ProductCategories slug for a given ingredient name.
    /// If currentCategory is already valid and not "other", keeps currentCategory.
    /// </summary>
    public static string InferCategory(string ingredientName, string? currentCategory = null)
    {
        if (!string.IsNullOrWhiteSpace(currentCategory) &&
            currentCategory != ProductCategories.Other &&
            ProductCategories.IsValid(currentCategory))
        {
            return currentCategory;
        }

        if (string.IsNullOrWhiteSpace(ingredientName))
        {
            return ProductCategories.Other;
        }

        var name = ingredientName.Trim().ToLowerInvariant();
        var tokens = name.Split([' ', ',', '.', '-', '(', ')', '%', '"', '\''], StringSplitOptions.RemoveEmptyEntries);

        // 1. Prepared meals / ready dishes
        if (ContainsWord(name, tokens, "борщ", "суп", "рагу", "плов", "запіканка", "котлети", "котлета", "омлет", "голубці", "вареники", "деруни", "сирники", "млинці", "готова страва", "готові страви", "готовий", "prepared meal", "cooked meal"))
        {
            return ProductCategories.PreparedMeals;
        }

        // 2. Sauces, oils & spices
        if (ContainsWord(name, tokens, "сіль", "salt", "олія", "oil", "перець", "pepper", "соус", "sauce", "масло рослинне", "оливкова олія",
            "паприка", "спеції", "spice", "кориця", "оцет", "vinegar", "майонез", "mayo", "кетчуп",
            "ketchup", "гірчиця", "mustard", "соєвий соус", "приправа", "лавровий лист", "каррі", "curry", "орегано", "базилік", "куркума", "сироп", "syrup"))
        {
            return ProductCategories.Sauces;
        }

        // 3. Dairy
        if (ContainsWord(name, tokens, "молоко", "milk", "сир", "cheese", "вершкове масло", "butter", "сметана", "sour cream",
            "кефір", "kefir", "йогурт", "yogurt", "творог", "cottage cheese", "вершки", "cream", "ряжанка", "моцарела", "пармезан", "сулугуні", "бринза"))
        {
            return ProductCategories.Dairy;
        }

        // 4. Meat & Fish
        if (ContainsWord(name, tokens, "м'ясо", "meat", "курка", "куряче", "куряча", "chicken", "фарш", "mince", "свинина", "pork", "яловичина", "beef",
            "телятина", "veal", "риба", "fish", "лосось", "salmon", "тунець", "tuna", "креветки", "shrimp", "філе", "filet", "fillet",
            "бекон", "bacon", "ковбаса", "sausage", "сосиски", "індичка", "turkey", "качка", "duck"))
        {
            return ProductCategories.MeatFish;
        }

        // 5. Vegetables & greens
        if (ContainsWord(name, tokens, "цибуля", "onion", "часник", "garlic", "морква", "carrot", "картопля", "potato", "помідор", "помідори", "томат", "томати", "tomato",
            "огірок", "огірки", "cucumber", "капуста", "cabbage", "зелень", "greens", "петрушка", "parsley", "кріп", "dill", "шпинат", "spinach",
            "салат", "lettuce", "кабачок", "zucchini", "баклажан", "eggplant", "брокколі", "broccoli", "гриби", "mushroom", "печериці"))
        {
            return ProductCategories.Vegetables;
        }

        // 6. Fruits & berries
        if (ContainsWord(name, tokens, "яблуко", "яблука", "apple", "банан", "банани", "banana", "лимон", "lemon", "лайм", "lime", "апельсин", "orange",
            "мандарин", "полуниця", "strawberry", "малина", "raspberry", "ягоди", "berries", "груша", "pear", "виноград", "grape", "авокадо", "avocado", "персик", "peach"))
        {
            return ProductCategories.Fruits;
        }

        // 7. Bread & Bakery
        if (ContainsWord(name, tokens, "хліб", "bread", "батон", "булочка", "булка", "bun", "лаваш", "піта", "pita", "багет", "baguette", "круасан"))
        {
            return ProductCategories.Bakery;
        }

        // 8. Pantry staples
        if (ContainsWord(name, tokens, "борошно", "flour", "рис", "rice", "гречка", "buckwheat", "макарони", "pasta", "спагеті", "spaghetti",
            "цукор", "sugar", "вівсянка", "oats", "oatmeal", "крупа", "квасоля", "beans", "горох", "peas", "сочевиця", "lentils", "дріжджі", "yeast", "крохмаль", "starch"))
        {
            return ProductCategories.Pantry;
        }

        // 9. Drinks
        if (ContainsWord(name, tokens, "вода", "water", "сік", "juice", "чай", "tea", "кава", "coffee", "морс", "компот"))
        {
            return ProductCategories.Drinks;
        }

        // 10. Alcohol
        if (ContainsWord(name, tokens, "вино", "wine", "пиво", "beer", "горілка", "vodka", "коньяк", "віскі", "whiskey", "ром", "rum"))
        {
            return ProductCategories.Alcohol;
        }

        // 11. Snacks & sweets
        if (ContainsWord(name, tokens, "шоколад", "chocolate", "печиво", "cookie", "biscuits", "цукерки", "цукерка", "candy", "горіхи", "nuts", "чипси", "chips", "барбарис"))
        {
            return ProductCategories.Snacks;
        }

        // 12. Frozen
        if (ContainsWord(name, tokens, "заморожені", "заморожена", "заморожений", "frozen"))
        {
            return ProductCategories.Frozen;
        }

        // 13. Canned & prepared
        if (ContainsWord(name, tokens, "консерва", "консервований", "консервована", "canned", "тушонка", "шпроти"))
        {
            return ProductCategories.CannedPrepared;
        }

        return ProductCategories.Other;
    }

    private static bool ContainsWord(string text, string[] tokens, params string[] keywords)
    {
        foreach (var k in keywords)
        {
            if (k.Contains(' '))
            {
                // Multi-word phrase: match full phrase with word boundaries or substring
                if (text.Contains(k, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                foreach (var t in tokens)
                {
                    if (string.Equals(t, k, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var stemT = IngredientDeductionHelper.StripEnding(t);
                    var stemK = IngredientDeductionHelper.StripEnding(k);

                    if (stemT.Length >= 4 && stemK.Length >= 4 && stemT == stemK)
                        return true;
                }
            }
        }
        return false;
    }
}
