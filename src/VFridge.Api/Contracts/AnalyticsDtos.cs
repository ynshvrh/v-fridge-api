namespace VFridge.Api.Contracts;

public sealed record AnalyticsSummary(
    IReadOnlyList<AnalyticsLeader> MostWasted,
    IReadOnlyList<FastestConsumed> FastestConsumed,
    IReadOnlyList<WeeklyTrend> WeeklyTrends);

public sealed record AnalyticsLeader(
    string ProductName,
    decimal TotalQuantity,
    int Occurrences,
    string Category);

public sealed record FastestConsumed(
    string ProductName,
    string Category,
    int AgeDays);

public sealed record WeeklyTrend(
    string WeekStart,
    int Consumed,
    int Wasted,
    int Expired);
