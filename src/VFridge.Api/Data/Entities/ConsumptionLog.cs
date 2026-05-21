using VFridge.Api.Contracts;

namespace VFridge.Api.Data.Entities;

public sealed class ConsumptionLog
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string ProductName { get; set; } = null!;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string Category { get; set; } = ProductCategories.Other;
    public string Status { get; set; } = ConsumptionStatus.Consumed;
    public int? AgeDays { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public static class ConsumptionStatus
{
    public const string Consumed = "consumed";
    public const string Wasted = "wasted";   // deleted while still fresh (user discarded)
    public const string Expired = "expired"; // deleted after expiry date
}
