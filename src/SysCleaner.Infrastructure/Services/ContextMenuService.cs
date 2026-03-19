using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Infrastructure.Services;

public sealed class ContextMenuService(IHistoryService historyService) : IContextMenuService
{
    private static readonly string[] ShellPaths =
    [
        @"*\shell",
        @"Directory\shell",
        @"Directory\Background\shell",
        @"Folder\shell",
        @"AllFilesystemObjects\shell",
        @"Drive\shell"
    ];

    public Task<IReadOnlyList<CleanupCandidate>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CleanupCandidate>>(() =>
        {
            var results = new List<CleanupCandidate>();
            foreach (var shellPath in ShellPaths)
            {
                ScanShellCommands(results, shellPath);
            }

            ScanHandlers(results, @"*\shellex\ContextMenuHandlers");
            ScanHandlers(results, @"Directory\shellex\ContextMenuHandlers");
            return results.OrderBy(x => x.Title).ToList();
        }, cancellationToken);
    }

    public async Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            using var key = Registry.ClassesRoot.CreateSubKey(candidate.Metadata, writable: true);
            key?.SetValue("LegacyDisable", string.Empty, RegistryValueKind.String);
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "ContextMenu", "Disable", candidate.Metadata, "Success", candidate.Title), cancellationToken);
            return new OperationResult(true, "已禁用右键菜单项。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "ContextMenu", "Disable", candidate.Metadata, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            DeleteClassesRootKey(candidate.Metadata);
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "ContextMenu", "Delete", candidate.Metadata, "Success", candidate.Title), cancellationToken);
            return new OperationResult(true, "已删除右键菜单项。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "ContextMenu", "Delete", candidate.Metadata, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    private static void ScanShellCommands(List<CleanupCandidate> results, string shellPath)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(shellPath);
        if (key is null)
        {
            return;
        }

        foreach (var childName in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(childName);
            using var commandKey = child?.OpenSubKey("command");
            var command = commandKey?.GetValue(string.Empty)?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var target = ScanUtilities.ExtractExecutablePath(command);
            var exists = ScanUtilities.PathExists(target);
            var title = ScanUtilities.SafeGetString(child!, "MUIVerb");
            if (string.IsNullOrWhiteSpace(title))
            {
                title = childName;
            }

            var protectedEntry = ScanUtilities.IsProtectedPublisher(command, title);
            results.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.ContextMenuEntry,
                title,
                target,
                shellPath,
                exists ? "命令目标存在" : "命令目标不存在",
                protectedEntry ? ItemHealth.Protected : exists ? ItemHealth.Healthy : ItemHealth.Broken,
                protectedEntry ? RiskLevel.Protected : exists ? RiskLevel.Review : RiskLevel.Safe,
                !protectedEntry,
                !protectedEntry,
                true,
                Metadata: $"{shellPath}\\{childName}"));
        }
    }

    private static void ScanHandlers(List<CleanupCandidate> results, string path)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(path);
        if (key is null)
        {
            return;
        }

        foreach (var childName in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(childName);
            var clsid = child?.GetValue(string.Empty)?.ToString() ?? string.Empty;
            var server = ResolveInprocServer(clsid);
            var exists = ScanUtilities.PathExists(server);
            results.Add(new CleanupCandidate(
                Guid.NewGuid().ToString("N"),
                CleanupCategory.ContextMenuEntry,
                childName,
                server,
                path,
                exists ? "Shell 扩展注册正常" : "Shell 扩展 DLL 不存在或 CLSID 无效",
                exists ? ItemHealth.Review : ItemHealth.Broken,
                exists ? RiskLevel.High : RiskLevel.Review,
                false,
                true,
                true,
                Metadata: $"{path}\\{childName}"));
        }
    }

    private static string ResolveInprocServer(string clsid)
    {
        if (string.IsNullOrWhiteSpace(clsid))
        {
            return string.Empty;
        }

        using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
        return key?.GetValue(string.Empty)?.ToString() ?? string.Empty;
    }

    private static void DeleteClassesRootKey(string relativePath)
    {
        var splitIndex = relativePath.LastIndexOf('\\');
        if (splitIndex <= 0)
        {
            return;
        }

        var parentPath = relativePath[..splitIndex];
        var child = relativePath[(splitIndex + 1)..];
        using var parent = Registry.ClassesRoot.OpenSubKey(parentPath, writable: true);
        parent?.DeleteSubKeyTree(child, throwOnMissingSubKey: false);
    }
}