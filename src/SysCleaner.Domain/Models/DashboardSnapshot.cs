namespace SysCleaner.Domain.Models;

public sealed record DashboardSnapshot(
    IReadOnlyList<ModuleSummary> SummaryItems,
    IReadOnlyList<DashboardRecommendation> Recommendations,
    IReadOnlyList<OperationLogEntry> RecentEntries,
    int TotalIssueCount,
    int RecentActionCount,
    string OverviewHeadline,
    string OverviewDetail);