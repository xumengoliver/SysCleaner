namespace SysCleaner.Domain.Models;

public sealed record WindowsUpdateFailureGroup(
    string ErrorCode,
    int Count,
    DateTime? LatestTime,
    string LatestTitle);