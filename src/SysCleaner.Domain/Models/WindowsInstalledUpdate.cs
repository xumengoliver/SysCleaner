namespace SysCleaner.Domain.Models;

public sealed record WindowsInstalledUpdate(
    string KbId,
    string Title,
    string UpdateType,
    bool IsSecurityUpdate,
    DateTime? InstalledOn,
    string InstalledBy,
    bool CanUninstall);