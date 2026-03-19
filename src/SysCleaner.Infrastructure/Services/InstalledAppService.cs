using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Infrastructure.Services;

public sealed class InstalledAppService(IHistoryService historyService) : IInstalledAppService
{
    public Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var apps = new List<InstalledApp>();

            foreach (var (root, path) in ScanUtilities.GetUninstallRoots())
            {
                using var baseKey = root.OpenSubKey(path);
                if (baseKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var subKey = baseKey.OpenSubKey(subKeyName, false);
                    if (subKey is null)
                    {
                        continue;
                    }

                    var displayName = ScanUtilities.SafeGetString(subKey, "DisplayName");
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        continue;
                    }

                    var publisher = ScanUtilities.SafeGetString(subKey, "Publisher");
                    var installLocation = ScanUtilities.SafeGetString(subKey, "InstallLocation");
                    var uninstallString = ScanUtilities.SafeGetString(subKey, "UninstallString");
                    var quietUninstallString = ScanUtilities.SafeGetString(subKey, "QuietUninstallString");
                    var version = ScanUtilities.SafeGetString(subKey, "DisplayVersion");
                    var registryPath = $"{root.Name}\\{path}\\{subKeyName}";
                    var systemComponent = ScanUtilities.SafeGetString(subKey, "SystemComponent") == "1";
                    var isProtected = systemComponent || ScanUtilities.IsProtectedPublisher(publisher, displayName);
                    var uninstallPath = ScanUtilities.ExtractExecutablePath(uninstallString);
                    var quietPath = ScanUtilities.ExtractExecutablePath(quietUninstallString);
                    var health = EvaluateHealth(uninstallPath, quietPath, installLocation, isProtected);

                    var app = new InstalledApp(
                        ScanUtilities.ToAppId(registryPath),
                        displayName,
                        publisher,
                        version,
                        installLocation,
                        uninstallString,
                        quietUninstallString,
                        registryPath,
                        systemComponent,
                        isProtected,
                        health,
                        health == ItemHealth.Broken ? "卸载入口可能已失效" : string.Empty);

                    apps.Add(app);
                }
            }

            return DeduplicateInstalledApps(apps)
                .OrderBy(x => x.DisplayName)
                .ThenBy(x => x.Publisher)
                .ThenBy(x => x.Version)
                .ToList();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<InstalledApp>> GetBrokenUninstallEntriesAsync(CancellationToken cancellationToken = default)
    {
        var apps = await GetInstalledAppsAsync(cancellationToken);
        return apps.Where(x => x.Health == ItemHealth.Broken).ToList();
    }

    public async Task<OperationResult> LaunchUninstallAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        try
        {
            var command = ResolvePreferredUninstallCommand(app);
            if (string.IsNullOrWhiteSpace(command))
            {
                return new OperationResult(false, "未找到卸载命令。");
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/C {command}")
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return new OperationResult(false, "启动卸载程序失败。");
            }

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Software", "LaunchUninstall", app.DisplayName, "Started", command), cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Software", "LaunchUninstall", app.DisplayName, "Success", "卸载程序已退出。"), cancellationToken);
                return new OperationResult(true, "卸载程序已结束，正在刷新列表。");
            }

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Software", "LaunchUninstall", app.DisplayName, "Failed", $"卸载程序退出码：{process.ExitCode}"), cancellationToken);
            return new OperationResult(false, $"卸载程序已退出，退出码：{process.ExitCode}");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Software", "LaunchUninstall", app.DisplayName, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> RemoveBrokenEntryAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        if (app.IsProtected)
        {
            return new OperationResult(false, "受保护条目不能删除。");
        }

        try
        {
            var firstSlash = app.RegistryPath.IndexOf('\\');
            var hiveName = app.RegistryPath[..firstSlash];
            var subPath = app.RegistryPath[(firstSlash + 1)..];
            var splitIndex = subPath.LastIndexOf('\\');
            var parentPath = subPath[..splitIndex];
            var subKeyName = subPath[(splitIndex + 1)..];
            using var root = hiveName.Contains("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ? Registry.CurrentUser : Registry.LocalMachine;
            using var parent = root.OpenSubKey(parentPath, writable: true);
            parent?.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "BrokenUninstall", "Delete", app.RegistryPath, "Success", app.DisplayName), cancellationToken);
            return new OperationResult(true, "已删除失效卸载条目。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "BrokenUninstall", "Delete", app.RegistryPath, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    private static ItemHealth EvaluateHealth(string uninstallPath, string quietPath, string installLocation, bool isProtected)
    {
        if (isProtected)
        {
            return ItemHealth.Protected;
        }

        var hasValidUninstall = ScanUtilities.PathExists(uninstallPath) || ScanUtilities.PathExists(quietPath);
        var hasInstallLocation = string.IsNullOrWhiteSpace(installLocation) || Directory.Exists(installLocation);

        if (hasValidUninstall)
        {
            return ItemHealth.Healthy;
        }

        return hasInstallLocation ? ItemHealth.Review : ItemHealth.Broken;
    }

    internal static IReadOnlyList<InstalledApp> DeduplicateInstalledApps(IEnumerable<InstalledApp> apps)
    {
        var groups = new Dictionary<string, List<InstalledApp>>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            var key = BuildDeduplicationKey(app);
            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
            }

            group.Add(app);
        }

        return groups.Values.Select(MergeDuplicateGroup).ToList();
    }

    internal static string ResolvePreferredUninstallCommand(InstalledApp app)
    {
        if (ScanUtilities.PathExists(ScanUtilities.ExtractExecutablePath(app.UninstallString)))
        {
            return app.UninstallString;
        }

        if (ScanUtilities.PathExists(ScanUtilities.ExtractExecutablePath(app.QuietUninstallString)))
        {
            return app.QuietUninstallString;
        }

        return !string.IsNullOrWhiteSpace(app.UninstallString)
            ? app.UninstallString
            : app.QuietUninstallString;
    }

    private static InstalledApp MergeDuplicateGroup(List<InstalledApp> group)
    {
        if (group.Count == 1)
        {
            return group[0];
        }

        var preferred = group
            .OrderByDescending(GetAppPriority)
            .ThenBy(app => app.RegistryPath, StringComparer.OrdinalIgnoreCase)
            .First();

        var uninstallString = SelectBestValue(group, app => app.UninstallString, preferExistingPath: true);
        var quietUninstallString = SelectBestValue(group, app => app.QuietUninstallString, preferExistingPath: true);
        var installLocation = SelectBestInstallLocation(group);
        var publisher = SelectFirstNonEmpty(group, app => app.Publisher, preferred.Publisher);
        var version = SelectFirstNonEmpty(group, app => app.Version, preferred.Version);
        var isSystemComponent = group.Any(app => app.IsSystemComponent);
        var isProtected = group.Any(app => app.IsProtected);
        var health = EvaluateHealth(
            ScanUtilities.ExtractExecutablePath(uninstallString),
            ScanUtilities.ExtractExecutablePath(quietUninstallString),
            installLocation,
            isProtected);

        return preferred with
        {
            Publisher = publisher,
            Version = version,
            InstallLocation = installLocation,
            UninstallString = uninstallString,
            QuietUninstallString = quietUninstallString,
            IsSystemComponent = isSystemComponent,
            IsProtected = isProtected,
            Health = health,
            Notes = health == ItemHealth.Broken ? "卸载入口可能已失效" : string.Empty
        };
    }

    private static int GetAppPriority(InstalledApp app)
    {
        var score = 0;
        var uninstallPath = ScanUtilities.ExtractExecutablePath(app.UninstallString);
        var quietPath = ScanUtilities.ExtractExecutablePath(app.QuietUninstallString);

        if (ScanUtilities.PathExists(uninstallPath))
        {
            score += 200;
        }
        else if (!string.IsNullOrWhiteSpace(app.UninstallString))
        {
            score += 60;
        }

        if (ScanUtilities.PathExists(quietPath))
        {
            score += 120;
        }
        else if (!string.IsNullOrWhiteSpace(app.QuietUninstallString))
        {
            score += 30;
        }

        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(app.Version))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(app.Publisher))
        {
            score += 5;
        }

        score += app.Health switch
        {
            ItemHealth.Healthy => 40,
            ItemHealth.Review => 15,
            ItemHealth.Protected => 10,
            _ => 0
        };

        return score;
    }

    private static string BuildDeduplicationKey(InstalledApp app)
    {
        var installLocation = ScanUtilities.ExpandPath(app.InstallLocation);
        var uninstallPath = ScanUtilities.ExtractExecutablePath(app.UninstallString);
        var quietPath = ScanUtilities.ExtractExecutablePath(app.QuietUninstallString);
        var anchor = !string.IsNullOrWhiteSpace(installLocation)
            ? installLocation
            : !string.IsNullOrWhiteSpace(uninstallPath)
                ? uninstallPath
                : quietPath;

        if (string.IsNullOrWhiteSpace(anchor)
            && string.IsNullOrWhiteSpace(app.Publisher)
            && string.IsNullOrWhiteSpace(app.Version))
        {
            anchor = app.RegistryPath;
        }

        return string.Join(
            "|",
            ScanUtilities.Normalize(app.DisplayName),
            ScanUtilities.Normalize(app.Publisher),
            ScanUtilities.Normalize(app.Version),
            ScanUtilities.Normalize(anchor));
    }

    private static string SelectBestInstallLocation(IEnumerable<InstalledApp> group)
    {
        var existing = group
            .Select(app => ScanUtilities.ExpandPath(app.InstallLocation))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));

        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return group
            .Select(app => ScanUtilities.ExpandPath(app.InstallLocation))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            ?? string.Empty;
    }

    private static string SelectBestValue(IEnumerable<InstalledApp> group, Func<InstalledApp, string> selector, bool preferExistingPath)
    {
        var values = group
            .Select(app => selector(app))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (preferExistingPath)
        {
            var existing = values.FirstOrDefault(value => ScanUtilities.PathExists(ScanUtilities.ExtractExecutablePath(value)));
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        return values[0];
    }

    private static string SelectFirstNonEmpty(IEnumerable<InstalledApp> group, Func<InstalledApp, string> selector, string fallback)
    {
        return group.Select(selector).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? fallback;
    }
}