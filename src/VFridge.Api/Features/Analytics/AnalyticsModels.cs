namespace VFridge.Api.Features.Analytics;

public sealed record AnalyticsSummaryResponse(
    IReadOnlyList<AnalyticsLeaderResponse> MostWasted,
    IReadOnlyList<FastestConsumedResponse> FastestConsumed,
    IReadOnlyList<WeeklyTrendResponse> WeeklyTrends);

public sealed record AnalyticsLeaderResponse(
    string ProductName,
    decimal TotalQuantity,
    int Occurrences,
    string Category);

public sealed record FastestConsumedResponse(
    string ProductName,
    string Category,
    int AgeDays);

public sealed record WeeklyTrendResponse(
    string WeekStart,
    int Consumed,
    int Wasted,
    int Expired);
