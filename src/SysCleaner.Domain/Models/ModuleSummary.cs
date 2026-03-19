namespace SysCleaner.Domain.Models;

public sealed record ModuleSummary(
    string Title,
    string Description,
    int Count,
    string Accent,
    string RouteKey);