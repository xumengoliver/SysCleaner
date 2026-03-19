using SysCleaner.Domain.Enums;

namespace SysCleaner.Domain.Models;

public sealed record CleanupCandidate(
    string Id,
    CleanupCategory Category,
    string Title,
    string TargetPath,
    string Source,
    string Evidence,
    ItemHealth Health,
    RiskLevel Risk,
    bool CanDisable,
    bool CanDelete,
    bool CanRollback,
    string RelatedAppId = "",
    string Metadata = "");