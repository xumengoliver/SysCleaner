using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Infrastructure.Services;

public sealed class StartupItemService(IHistoryService historyService) : IStartupItemService
{
    public Task<IReadOnlyList<CleanupCandidate>> GetStartupItemsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CleanupCandidate>>(() =>
        {
            var results = new List<CleanupCandidate>();
            ScanRegistryStartup(results, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
            ScanRegistryStartup(results, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run");
            ScanStartupFolder(results, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            ScanStartupFolder(results, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            return results.OrderBy(x => x.Title).ToList();
        }, cancellationToken);
    }

    public async Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            if (candidate.Metadata.StartsWith("startup-file|", StringComparison.OrdinalIgnoreCase))
            {
                var path = candidate.Metadata[13..];
                var disabledPath = path + ".disabled";
                File.Move(path, disabledPath, overwrite: true);
            }
            else if (candidate.Metadata.StartsWith("startup-registry|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = candidate.Metadata.Split('|');
                DisableRegistryValue(parts[1], parts[2]);
            }

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Startup", "Disable", candidate.TargetPath, "Success", candidate.Title), cancellationToken);
            return new OperationResult(true, "已禁用启动项。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Startup", "Disable", candidate.TargetPath, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            if (candidate.Metadata.StartsWith("startup-file|", StringComparison.OrdinalIgnoreCase))
            {
                var deletion = await PathDeletionHelper.DeleteAsync(candidate.Metadata[13..], recursive: false, cancellationToken);
                if (!deletion.Success)
                {
                    return new OperationResult(false, deletion.Message);
                }
            }
            else if (candidate.Metadata.StartsWith("startup-registry|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = candidate.Metadata.Split('|');
                DeleteRegistryValue(parts[1], parts[2]);
            }

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Startup", "Delete", candidate.TargetPath, "Success", candidate.Title), cancellationToken);
            return new OperationResult(true, "已删除启动项。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Startup", "Delete", candidate.TargetPath, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    private static void ScanRegistryStartup(List<CleanupCandidate> results, RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            var value = key.GetValue(name)?.ToString() ?? string.Empty;
            var target = ScanUtilities.ExtractExecutablePath(value);
            var exists = ScanUtilities.PathExists(target);
            var protectedEntry = ScanUtilities.IsProtectedPublisher(value, name);
            var health = protectedEntry ? ItemHealth.Protected : exists ? ItemHealth.Healthy : ItemHealth.Broken;
            results.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.StartupEntry,
                name,
                target,
                $"{root.Name}\\{path}",
                exists ? "启动命令有效" : "启动目标不存在",
                health,
                protectedEntry ? RiskLevel.Protected : exists ? RiskLevel.Review : RiskLevel.Safe,
                !protectedEntry,
                !protectedEntry,
                true,
                Metadata: $"startup-registry|{root.Name}\\{path}|{name}"));
        }
    }

    private static void ScanStartupFolder(List<CleanupCandidate> results, string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path))
        {
            var target = ScanUtilities.TryReadShortcutTarget(file);
            var exists = string.IsNullOrWhiteSpace(target) || ScanUtilities.PathExists(target);
            results.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.StartupEntry,
                Path.GetFileName(file),
                target,
                path,
                exists ? "启动文件存在" : "快捷方式目标不存在",
                exists ? ItemHealth.Healthy : ItemHealth.Broken,
                exists ? RiskLevel.Review : RiskLevel.Safe,
                true,
                true,
                true,
                Metadata: $"startup-file|{file}"));
        }
    }

    private static void DisableRegistryValue(string keyPath, string valueName)
    {
        var (root, subPath) = OpenRoot(keyPath);
        using var readKey = root.OpenSubKey(subPath);
        var value = readKey?.GetValue(valueName);
        using var disabledKey = Registry.CurrentUser.CreateSubKey(@"Software\SysCleaner\DisabledStartup");
        disabledKey?.SetValue($"{keyPath}|{valueName}", value?.ToString() ?? string.Empty);
        using var writeKey = root.OpenSubKey(subPath, writable: true);
        writeKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static void DeleteRegistryValue(string keyPath, string valueName)
    {
        var (root, subPath) = OpenRoot(keyPath);
        using var writeKey = root.OpenSubKey(subPath, writable: true);
        writeKey?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static (RegistryKey Root, string SubPath) OpenRoot(string keyPath)
    {
        const string hkcu = "HKEY_CURRENT_USER\\";
        if (keyPath.StartsWith(hkcu, StringComparison.OrdinalIgnoreCase))
        {
            return (Registry.CurrentUser, keyPath[hkcu.Length..]);
        }

        const string hklm = "HKEY_LOCAL_MACHINE\\";
        return (Registry.LocalMachine, keyPath[hklm.Length..]);
    }
}