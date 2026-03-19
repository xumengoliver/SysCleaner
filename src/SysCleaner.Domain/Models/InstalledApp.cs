using SysCleaner.Domain.Enums;

namespace SysCleaner.Domain.Models;

public sealed record InstalledApp(
    string Id,
    string DisplayName,
    string Publisher,
    string Version,
    string InstallLocation,
    string UninstallString,
    string QuietUninstallString,
    string RegistryPath,
    bool IsSystemComponent,
    bool IsProtected,
    ItemHealth Health,
    string Notes)
{
    public string ScopeLabel => RegistryPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
        ? "当前用户"
        : "所有用户";

    public string ArchitectureLabel => RegistryPath.Contains("\\WOW6432Node\\", StringComparison.OrdinalIgnoreCase)
        ? "32 位卸载项"
        : RegistryPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
            ? "64 位卸载项"
            : "用户卸载项";

    public string SourceSummary => $"{ScopeLabel} / {ArchitectureLabel}";

    public bool IsCurrentUserInstall => RegistryPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase);

    public int SourceSortKey => IsCurrentUserInstall
        ? 0
        : RegistryPath.Contains("\\WOW6432Node\\", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 1;

    public bool HasUninstallCommand => !string.IsNullOrWhiteSpace(UninstallString) || !string.IsNullOrWhiteSpace(QuietUninstallString);

    public bool IsLikelyUninstallable => HasUninstallCommand && Health != ItemHealth.Broken;

    public int HealthSortKey => Health switch
    {
        ItemHealth.Broken => 0,
        ItemHealth.Review => 1,
        ItemHealth.Healthy => 2,
        ItemHealth.Protected => 3,
        _ => 4
    };

    public string DisplayLabel
    {
        get
        {
            var versionText = string.IsNullOrWhiteSpace(Version) ? "未标注版本" : Version;
            return $"{DisplayName} ({versionText} | {SourceSummary})";
        }
    }
}