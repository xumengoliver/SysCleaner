using SysCleaner.Domain.Enums;

namespace SysCleaner.Domain.Models;

public sealed record SystemRepairItem(
    string Id,
    string Title,
    string Description,
    string DetectionSummary,
    string Recommendation,
    ItemHealth Health,
    bool RequiresExplorerRestart,
    bool RequiresSignOut);