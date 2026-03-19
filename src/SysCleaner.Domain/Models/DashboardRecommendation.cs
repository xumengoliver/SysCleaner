namespace SysCleaner.Domain.Models;

public sealed record DashboardRecommendation(
    string Title,
    string Detail,
    string PriorityLabel,
    string ReasonLabel,
    string StrategyLabel,
    string EntryLabel,
    bool PauseRepeatedExecution,
    bool RequiresManualConfirmation,
    string Accent,
    string RouteKey);