using System.Globalization;

namespace VFridge.Api.Services;

public sealed record NutritionPer100g(
    double Calories,
    double Protein,
    double Fat,
    double Carbs,
    double DefaultPieceGrams = 100);

public static class NutritionCalculator
{
    // Comprehensive food nutrition database per 100g / 100ml
    private static readonly Dictionary<string, NutritionPer100g> Database = new(StringComparer.OrdinalIgnoreCase)
    {
        // Meat & Poultry
        ["курка"] = new(165, 31.0, 3.6, 0.0, 150),
        ["куряче філе"] = new(165, 31.0, 3.6, 0.0, 200),
        ["куряча грудка"] = new(165, 31.0, 3.6, 0.0, 200),
        ["куряче стегно"] = new(210, 24.0, 12.0, 0.0, 150),
        ["індичка"] = new(144, 30.0, 2.0, 0.0, 200),
        ["філе індички"] = new(144, 30.0, 2.0, 0.0, 200),
        ["яловичина"] = new(250, 26.0, 17.0, 0.0, 200),
        ["яловичий фарш"] = new(240, 24.0, 16.0, 0.0, 200),
        ["свинина"] = new(242, 27.0, 14.0, 0.0, 200),
        ["свинячий фарш"] = new(260, 22.0, 19.0, 0.0, 200),
        ["фарш"] = new(250, 23.0, 17.0, 0.0, 200),
        ["бекон"] = new(450, 14.0, 42.0, 1.0, 30),
        ["сосиски"] = new(260, 11.0, 23.0, 2.0, 60),
        ["шинка"] = new(145, 21.0, 6.0, 1.5, 50),

        // Fish & Seafood
        ["лосось"] = new(208, 20.0, 13.0, 0.0, 150),
        ["сьомга"] = new(208, 20.0, 13.0, 0.0, 150),
        ["форель"] = new(148, 20.8, 6.6, 0.0, 150),
        ["хек"] = new(86, 18.0, 1.3, 0.0, 150),
        ["минтай"] = new(72, 16.0, 0.7, 0.0, 150),
        ["тунець"] = new(130, 28.0, 1.0, 0.0, 150),
        ["риба"] = new(120, 20.0, 4.0, 0.0, 150),
        ["рибне філе"] = new(110, 19.0, 3.0, 0.0, 150),
        ["креветки"] = new(99, 24.0, 0.3, 0.2, 15),
        ["мідії"] = new(86, 12.0, 2.2, 3.7, 10),

        // Eggs & Dairy
        ["яйце"] = new(143, 12.6, 9.5, 0.7, 50), // 1 egg ~ 50g -> 72 kcal, 6.3g P, 4.8g F
        ["яйця"] = new(143, 12.6, 9.5, 0.7, 50),
        ["яєчний білок"] = new(52, 11.0, 0.2, 0.7, 35),
        ["яєчний жовток"] = new(322, 16.0, 27.0, 3.6, 15),
        ["молоко"] = new(60, 3.2, 3.2, 4.8, 200),
        ["кефір"] = new(53, 3.0, 2.5, 4.0, 200),
        ["сметана"] = new(206, 2.8, 20.0, 3.2, 30),
        ["вершки"] = new(200, 2.8, 20.0, 3.7, 50),
        ["йогурт"] = new(65, 4.5, 3.0, 5.0, 150),
        ["грецький йогурт"] = new(97, 9.0, 5.0, 4.0, 150),
        ["сир"] = new(350, 25.0, 28.0, 1.5, 30),
        ["твердий сир"] = new(360, 25.0, 29.0, 1.5, 30),
        ["пармезан"] = new(431, 38.0, 29.0, 4.1, 20),
        ["моцарела"] = new(280, 22.0, 22.0, 2.2, 50),
        ["сулугуні"] = new(290, 20.0, 22.0, 0.0, 50),
        ["фета"] = new(264, 14.0, 21.0, 4.1, 30),
        ["сир кисломолочний"] = new(120, 18.0, 5.0, 3.0, 100),
        ["творог"] = new(120, 18.0, 5.0, 3.0, 100),
        ["домашній сир"] = new(120, 18.0, 5.0, 3.0, 100),
        ["масло"] = new(717, 0.8, 81.0, 0.7, 10),
        ["вершкове масло"] = new(717, 0.8, 81.0, 0.7, 10),

        // Grains, Pasta & Bakery
        ["рис"] = new(130, 2.7, 0.3, 28.0, 100),
        ["рис басматі"] = new(130, 2.7, 0.3, 28.0, 100),
        ["гречка"] = new(132, 4.5, 1.0, 25.0, 100),
        ["вівсянка"] = new(389, 17.0, 7.0, 66.0, 50),
        ["вівсяні пластівці"] = new(389, 17.0, 7.0, 66.0, 50),
        ["макарони"] = new(158, 5.8, 0.9, 31.0, 100),
        ["паста"] = new(158, 5.8, 0.9, 31.0, 100),
        ["спагеті"] = new(158, 5.8, 0.9, 31.0, 100),
        ["кускус"] = new(112, 3.8, 0.2, 23.0, 100),
        ["булгур"] = new(120, 3.5, 0.2, 26.0, 100),
        ["квасоля"] = new(127, 8.7, 0.5, 23.0, 100),
        ["горох"] = new(118, 8.3, 0.5, 21.0, 100),
        ["нут"] = new(164, 8.9, 2.6, 27.0, 100),
        ["сочевиця"] = new(116, 9.0, 0.4, 20.0, 100),
        ["борошно"] = new(364, 10.0, 1.0, 76.0, 100),
        ["пшеничне борошно"] = new(364, 10.0, 1.0, 76.0, 100),
        ["хліб"] = new(265, 9.0, 3.2, 49.0, 40),
        ["лаваш"] = new(270, 8.0, 1.0, 56.0, 50),

        // Vegetables & Mushrooms
        ["картопля"] = new(77, 2.0, 0.1, 17.0, 120),
        ["морква"] = new(41, 0.9, 0.2, 9.6, 100),
        ["цибуля"] = new(40, 1.1, 0.1, 9.3, 100),
        ["ріпчаста цибуля"] = new(40, 1.1, 0.1, 9.3, 100),
        ["зелена цибуля"] = new(32, 1.8, 0.2, 7.3, 20),
        ["часник"] = new(149, 6.4, 0.5, 33.0, 5),
        ["помідор"] = new(18, 0.9, 0.2, 3.9, 120),
        ["помідори"] = new(18, 0.9, 0.2, 3.9, 120),
        ["томати"] = new(18, 0.9, 0.2, 3.9, 120),
        ["томатна паста"] = new(82, 4.3, 0.5, 19.0, 30),
        ["огірок"] = new(15, 0.7, 0.1, 3.6, 100),
        ["огірки"] = new(15, 0.7, 0.1, 3.6, 100),
        ["капуста"] = new(25, 1.3, 0.1, 5.8, 100),
        ["пекінська капуста"] = new(16, 1.2, 0.2, 3.2, 100),
        ["броколі"] = new(34, 2.8, 0.4, 6.6, 100),
        ["цвітна капуста"] = new(25, 1.9, 0.3, 5.0, 100),
        ["кабачок"] = new(17, 1.2, 0.3, 3.1, 150),
        ["цукіні"] = new(17, 1.2, 0.3, 3.1, 150),
        ["баклажан"] = new(25, 1.0, 0.2, 6.0, 150),
        ["перець"] = new(31, 1.0, 0.3, 6.0, 120),
        ["болгарський перець"] = new(31, 1.0, 0.3, 6.0, 120),
        ["печериці"] = new(22, 3.1, 0.3, 3.3, 50),
        ["гриби"] = new(22, 3.1, 0.3, 3.3, 50),
        ["буряк"] = new(43, 1.6, 0.2, 9.6, 150),
        ["шпинат"] = new(23, 2.9, 0.4, 3.6, 50),
        ["зелень"] = new(30, 2.5, 0.5, 5.0, 20),
        ["петрушка"] = new(36, 3.0, 0.8, 6.3, 15),
        ["кріп"] = new(38, 2.5, 0.5, 7.0, 15),
        ["авокадо"] = new(160, 2.0, 15.0, 9.0, 150),

        // Fruits
        ["яблуко"] = new(52, 0.3, 0.2, 14.0, 150),
        ["яблука"] = new(52, 0.3, 0.2, 14.0, 150),
        ["банан"] = new(89, 1.1, 0.3, 23.0, 120),
        ["банани"] = new(89, 1.1, 0.3, 23.0, 120),
        ["лимон"] = new(29, 1.1, 0.3, 9.0, 80),
        ["апельсин"] = new(47, 0.9, 0.1, 12.0, 150),
        ["персик"] = new(39, 0.9, 0.3, 9.5, 120),
        ["полуниця"] = new(32, 0.7, 0.3, 7.7, 100),
        ["малина"] = new(52, 1.2, 0.6, 12.0, 100),
        ["ягоди"] = new(40, 1.0, 0.4, 9.0, 100),

        // Oils & Sauces
        ["олія"] = new(884, 0.0, 100.0, 0.0, 15),
        ["соняшникова олія"] = new(884, 0.0, 100.0, 0.0, 15),
        ["оливкова олія"] = new(884, 0.0, 100.0, 0.0, 15),
        ["цукор"] = new(387, 0.0, 0.0, 100.0, 10),
        ["мед"] = new(304, 0.3, 0.0, 82.0, 20),
        ["соєвий соус"] = new(53, 8.0, 0.1, 4.9, 15),
        ["гірчиця"] = new(66, 4.0, 3.0, 6.0, 10),
        ["кетчуп"] = new(112, 1.3, 0.1, 26.0, 20),
        ["майонез"] = new(680, 1.0, 75.0, 2.5, 20),
        ["горіхи"] = new(650, 15.0, 65.0, 14.0, 30),
        ["волоські горіхи"] = new(654, 15.2, 65.2, 13.7, 30),
        ["арахіс"] = new(567, 26.0, 49.0, 16.0, 30)
    };

    public static NutritionPer100g? FindNutrition(string cleanIngredientName)
    {
        if (string.IsNullOrWhiteSpace(cleanIngredientName)) return null;

        var lower = cleanIngredientName.Trim().ToLowerInvariant();

        // 1. Exact match
        if (Database.TryGetValue(lower, out var exact))
        {
            return exact;
        }

        // 2. Token / word boundary matching (check 2-word combinations first, then single tokens)
        var tokens = lower.Split([' ', ',', '.', '-', '(', ')', '%', '"', '\''], StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var pair = $"{tokens[i]} {tokens[i + 1]}";
            if (Database.TryGetValue(pair, out var pairMatch))
                return pairMatch;
        }

        foreach (var t in tokens)
        {
            if (Database.TryGetValue(t, out var tokenMatch))
                return tokenMatch;

            // Safe stem matching against database keys (minimum stem length: 4)
            var stem = IngredientDeductionHelper.StripEnding(t);
            if (stem.Length >= 4)
            {
                foreach (var (key, val) in Database)
                {
                    if (key.Contains(' ')) continue;
                    var keyStem = IngredientDeductionHelper.StripEnding(key);
                    if (keyStem.Length >= 4 && keyStem == stem)
                        return val;
                }
            }
        }

        return null;
    }

    public static (int Calories, decimal Protein, decimal Fat, decimal Carbs) CalculateNutrition(
        IEnumerable<ParsedIngredient> ingredients,
        int portions = 1)
    {
        if (portions <= 0) portions = 1;

        double totalCalories = 0;
        double totalProtein = 0;
        double totalFat = 0;
        double totalCarbs = 0;

        foreach (var ing in ingredients)
        {
            var info = FindNutrition(ing.CleanName);
            if (info is null)
            {
                // Fallback default estimation for unknown food: 100 kcal / 100g
                info = new NutritionPer100g(100, 3.0, 2.0, 15.0, 100);
            }

            var weightGrams = EstimateWeightInGrams(ing.Quantity, ing.Unit, info.DefaultPieceGrams);

            var factor = weightGrams / 100.0;
            totalCalories += info.Calories * factor;
            totalProtein += info.Protein * factor;
            totalFat += info.Fat * factor;
            totalCarbs += info.Carbs * factor;
        }

        var portionCalories = (int)Math.Round(totalCalories / portions);
        var portionProt = Math.Round((decimal)(totalProtein / portions), 1);
        var portionFat = Math.Round((decimal)(totalFat / portions), 1);
        var portionCarbs = Math.Round((decimal)(totalCarbs / portions), 1);

        return (portionCalories, portionProt, portionFat, portionCarbs);
    }

    public static double EstimateWeightInGrams(decimal? quantity, string? unit, double defaultPieceGrams)
    {
        var qty = (double)(quantity ?? 1);
        if (qty <= 0) qty = 1;

        var normUnit = IngredientDeductionHelper.NormalizeUnit(unit);

        return normUnit switch
        {
            "g" => qty,
            "kg" => qty * 1000,
            "ml" => qty,
            "l" => qty * 1000,
            "pcs" or "шт" => qty * defaultPieceGrams,
            "ст.л." => qty * 15,
            "ч.л." => qty * 5,
            "дрібка" => qty * 1,
            "зубчик" => qty * 5,
            _ => qty * (defaultPieceGrams > 0 ? defaultPieceGrams : 100)
        };
    }
}
