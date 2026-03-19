namespace SysCleaner.Domain.Models;

public sealed record WindowsUpdateServiceState(
    string ServiceName,
    bool Exists,
    bool IsRunning,
    bool IsAutoStart);