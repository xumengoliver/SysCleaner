using SysCleaner.Domain.Enums;

namespace SysCleaner.Domain.Models;

public sealed record LockInfo(
    string Id,
    string TargetPath,
    string HolderName,
    int ProcessId,
    string HolderPath,
    string Publisher,
    LockKind Kind,
    RiskLevel Risk,
    bool CanTerminate,
    bool CanStopService,
    string Recommendation,
    string Notes);