using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Features.Nutrition;

public sealed record DailyNutritionResponse(
    string Date,
    NutritionTargetsResponse Targets,
    NutritionSummaryResponse Summary,
    IReadOnlyList<NutritionLogResponse> Logs);

public sealed record NutritionTargetsResponse(
    int? Calories,
    decimal? Protein,
    decimal? Fat,
    decimal? Carbs);

public sealed record NutritionSummaryResponse(
    int Calories,
    decimal Protein,
    decimal Fat,
    decimal Carbs);

public sealed record NutritionLogResponse(
    long Id,
    string MealType,
    string FoodName,
    decimal? Quantity,
    string? Unit,
    int Calories,
    decimal Protein,
    decimal Fat,
    decimal Carbs,
    DateTime LoggedAt);

public sealed class LogFoodRequest
{
    [Required]
    public string Date { get; set; } = null!;

    [Required]
    [RegularExpression("^(breakfast|lunch|dinner|snack)$", ErrorMessage = "MealType must be breakfast, lunch, dinner, or snack.")]
    public string MealType { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string FoodName { get; set; } = null!;

    public decimal? Quantity { get; set; }

    [MaxLength(20)]
    public string? Unit { get; set; }

    [Range(0, 10000, ErrorMessage = "Calories must be positive and less than 10000.")]
    public int Calories { get; set; }

    [Range(0, 1000, ErrorMessage = "Protein must be positive and less than 1000.")]
    public decimal Protein { get; set; }

    [Range(0, 1000, ErrorMessage = "Fat must be positive and less than 1000.")]
    public decimal Fat { get; set; }

    [Range(0, 1000, ErrorMessage = "Carbs must be positive and less than 1000.")]
    public decimal Carbs { get; set; }

    public int? ProductId { get; set; }
}

public sealed class UpdateLogRequest
{
    [Required]
    [RegularExpression("^(breakfast|lunch|dinner|snack)$", ErrorMessage = "MealType must be breakfast, lunch, dinner, or snack.")]
    public string MealType { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string FoodName { get; set; } = null!;

    public decimal? Quantity { get; set; }

    [MaxLength(20)]
    public string? Unit { get; set; }

    [Range(0, 10000, ErrorMessage = "Calories must be positive and less than 10000.")]
    public int Calories { get; set; }

    [Range(0, 1000, ErrorMessage = "Protein must be positive and less than 1000.")]
    public decimal Protein { get; set; }

    [Range(0, 1000, ErrorMessage = "Fat must be positive and less than 1000.")]
    public decimal Fat { get; set; }

    [Range(0, 1000, ErrorMessage = "Carbs must be positive and less than 1000.")]
    public decimal Carbs { get; set; }
}

public sealed class SetTargetsRequest
{
    [Range(0, 10000, ErrorMessage = "Calories must be positive and less than 10000.")]
    public int? Calories { get; set; }

    [Range(0, 1000, ErrorMessage = "Protein must be positive and less than 1000.")]
    public decimal? Protein { get; set; }

    [Range(0, 1000, ErrorMessage = "Fat must be positive and less than 1000.")]
    public decimal? Fat { get; set; }

    [Range(0, 1000, ErrorMessage = "Carbs must be positive and less than 1000.")]
    public decimal? Carbs { get; set; }
}
