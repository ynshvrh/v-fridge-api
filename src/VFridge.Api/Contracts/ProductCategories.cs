namespace VFridge.Api.Contracts;

/// <summary>
/// Fixed product-category catalog. The DB enforces this set via a CHECK constraint
/// (see Migrations/002_categories.sql). Adding a slug means both updating
/// <see cref="All"/> here AND extending the constraint via a new migration.
/// </summary>
public static class ProductCategories
{
    public const string Dairy = "dairy";
    public const string MeatFish = "meat-fish";
    public const string Vegetables = "vegetables";
    public const string Fruits = "fruits";
    public const string Bakery = "bakery";
    public const string Pantry = "pantry";
    public const string Snacks = "snacks";
    public const string Drinks = "drinks";
    public const string Alcohol = "alcohol";
    public const string Sauces = "sauces";
    public const string Frozen = "frozen";
    public const string CannedPrepared = "canned-prepared";
    public const string Other = "other";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Dairy] = "Dairy",
        [MeatFish] = "Meat & fish",
        [Vegetables] = "Vegetables & greens",
        [Fruits] = "Fruits & berries",
        [Bakery] = "Bread & bakery",
        [Pantry] = "Pantry staples",
        [Snacks] = "Snacks & sweets",
        [Drinks] = "Drinks",
        [Alcohol] = "Alcohol",
        [Sauces] = "Sauces, oils & spices",
        [Frozen] = "Frozen",
        [CannedPrepared] = "Canned & ready-to-eat",
        [Other] = "Other",
    };

    public static IReadOnlyCollection<string> All => Labels.Keys.ToList();

    public static bool IsValid(string slug) => Labels.ContainsKey(slug);

    public static string Label(string slug) => Labels.TryGetValue(slug, out var l) ? l : "Other";
}
