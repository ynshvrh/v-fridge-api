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

        // 1. Sauces, oils & spices (олії, спеції, соуси, сіль, перець, олія, оливкова олія, гірчиця, майонез, кетчуп)
        if (ContainsAny(name, "сіль", "salt", "олія", "oil", "перець", "pepper", "соус", "sauce", "масло рослинне", "оливкова",
            "оливка", "оливкова олія", "паприка", "спеції", "spice", "кориця", "оцет", "vinegar", "майонез", "mayo", "кетчуп",
            "ketchup", "гірчиця", "mustard", "соєвий", "soy", "приправа", "лавровий", "каррі", "curry", "орегано", "базилік", "паприка", "куркума"))
        {
            return ProductCategories.Sauces;
        }

        // 2. Dairy (молоко, сир, масло, сметана, кефір, йогурт, творог, вершки)
        if (ContainsAny(name, "молоко", "milk", "сир", "cheese", "вершкове масло", "butter", "сметана", "sour cream",
            "кефір", "kefir", "йогурт", "yogurt", "творог", "cottage cheese", "вершки", "cream", "ряжанка", "моцарела", "пармезан", "сулугуні", "бринза"))
        {
            return ProductCategories.Dairy;
        }

        // 3. Meat & Fish (м'ясо, курка, куряче, фарш, свинина, яловичина, телятина, риба, лосось, тунець, креветки, філе, бекон, ковбаса, сосиски)
        if (ContainsAny(name, "м'ясо", "meat", "курка", "куряче", "chicken", "фарш", "mince", "свинина", "pork", "яловичина", "beef",
            "телятина", "veal", "риба", "fish", "лосось", "salmon", "тунець", "tuna", "креветки", "shrimp", "філе", "filet", "fillet",
            "бекон", "bacon", "ковбаса", "sausage", "сосиски", "індичка", "turkey", "качка", "duck"))
        {
            return ProductCategories.MeatFish;
        }

        // 4. Vegetables & greens (цибуля, часник, морква, картопля, помідор, томат, огірок, капуста, перець болгарський, зелень, петрушка, кріп, шпинат, салат, кабачок, баклажан, брокколі, цвітна капуста, гриби, печериці)
        if (ContainsAny(name, "цибуля", "onion", "часник", "garlic", "морква", "carrot", "картопля", "potato", "помідор", "томат", "tomato",
            "огірок", "cucumber", "капуста", "cabbage", "зелень", "greens", "петрушка", "parsley", "кріп", "dill", "шпинат", "spinach",
            "салат", "lettuce", "кабачок", "zucchini", "баклажан", "eggplant", "брокколі", "broccoli", "гриби", "mushroom", "печериці"))
        {
            return ProductCategories.Vegetables;
        }

        // 5. Fruits & berries (яблуко, банан, лимон, лайм, апельсин, мандарин, полуниця, малина, ягоди, груша, виноград, авокадо)
        if (ContainsAny(name, "яблуко", "apple", "банан", "banana", "лимон", "lemon", "лайм", "lime", "апельсин", "orange",
            "мандарин", "полуниця", "strawberry", "малина", "raspberry", "ягоди", "berries", "груша", "pear", "виноград", "grape", "авокадо", "avocado"))
        {
            return ProductCategories.Fruits;
        }

        // 6. Bread & Bakery (хліб, батон, булочка, лаваш, піта, тост, багет)
        if (ContainsAny(name, "хліб", "bread", "батон", "булочка", "bun", "лаваш", "піта", "pita", "багет", "baguette", "круасан"))
        {
            return ProductCategories.Bakery;
        }

        // 7. Pantry staples (борошно, рис, гречка, макарони, спагеті, паста, цукор, вівсянка, крупа, квасоля, горох, сочевиця, дріжджі, крохмаль)
        if (ContainsAny(name, "борошно", "flour", "рис", "rice", "гречка", "buckwheat", "макарони", "pasta", "спагеті", "spaghetti",
            "цукор", "sugar", "вівсянка", "oats", "oatmeal", "крупа", "квасоля", "beans", "горох", "peas", "сочевиця", "lentils", "дріжджі", "yeast", "крохмаль", "starch"))
        {
            return ProductCategories.Pantry;
        }

        // 8. Drinks (вода, сік, чай, кава, морс, компот)
        if (ContainsAny(name, "вода", "water", "сік", "juice", "чай", "tea", "кава", "coffee", "морс", "компот"))
        {
            return ProductCategories.Drinks;
        }

        // 9. Alcohol (вино, пиво, горілка, коньяк, віскі, ром)
        if (ContainsAny(name, "вино", "wine", "пиво", "beer", "горілка", "vodka", "коньяк", "віскі", "whiskey", "ром", "rum"))
        {
            return ProductCategories.Alcohol;
        }

        // 10. Snacks & sweets (шоколад, печиво, цукерки, горіхи, чипси)
        if (ContainsAny(name, "шоколад", "chocolate", "печиво", "cookie", "biscuits", "цукерки", "candy", "горіхи", "nuts", "чипси", "chips"))
        {
            return ProductCategories.Snacks;
        }

        // 11. Frozen (заморожені, заморожена)
        if (ContainsAny(name, "заморожен", "frozen"))
        {
            return ProductCategories.Frozen;
        }

        // 12. Canned & prepared (консерва, тушонка, шпроти, кукурудза консервована, горошок консервований)
        if (ContainsAny(name, "консерв", "canned", "тушонка", "шпроти"))
        {
            return ProductCategories.CannedPrepared;
        }

        return ProductCategories.Other;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var k in keywords)
        {
            if (text.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
