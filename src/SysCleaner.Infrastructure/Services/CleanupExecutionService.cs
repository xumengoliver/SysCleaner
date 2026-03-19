using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Infrastructure.Services;

public sealed class CleanupExecutionService(IHistoryService historyService) : ICleanupExecutionService
{
    public async Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
    {
        if (!candidate.CanDelete)
        {
            return new OperationResult(false, "该候选项不允许删除。");
        }

        try
        {
            if (candidate.Metadata.StartsWith("registry-value|", StringComparison.OrdinalIgnoreCase))
            {
                DeleteRegistryValue(candidate.Metadata);
            }
            else if (candidate.Metadata.StartsWith("registry-key|", StringComparison.OrdinalIgnoreCase))
            {
                DeleteRegistryKey(candidate.Metadata);
            }
            else if (File.Exists(candidate.TargetPath))
            {
                var deletion = await PathDeletionHelper.DeleteAsync(candidate.TargetPath, recursive: false, cancellationToken);
                if (!deletion.Success)
                {
                    return new OperationResult(false, deletion.Message);
                }
            }
            else if (Directory.Exists(candidate.TargetPath))
            {
                var deletion = await PathDeletionHelper.DeleteAsync(candidate.TargetPath, recursive: true, cancellationToken);
                if (!deletion.Success)
                {
                    return new OperationResult(false, deletion.Message);
                }
            }
            else
            {
                return new OperationResult(false, "目标不存在或当前类型未实现删除。");
            }

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Cleanup", "Delete", candidate.TargetPath, "Success", candidate.Title), cancellationToken);
            return new OperationResult(true, "删除完成。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Cleanup", "Delete", candidate.TargetPath, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    private static void DeleteRegistryValue(string metadata)
    {
        var parts = metadata.Split('|');
        if (parts.Length < 3)
        {
            return;
        }

        var keyPath = parts[1];
        var valueName = parts[2];
        var (root, subPath) = OpenRegistryKey(keyPath);

        using var key = root.OpenSubKey(subPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static void DeleteRegistryKey(string metadata)
    {
        var parts = metadata.Split('|');
        if (parts.Length < 2)
        {
            return;
        }

        var keyPath = parts[1];
        var (root, subPath) = OpenRegistryKey(keyPath);
        if (string.IsNullOrWhiteSpace(subPath))
        {
            return;
        }

        var splitIndex = subPath.LastIndexOf('\\');
        var parentPath = splitIndex >= 0 ? subPath[..splitIndex] : string.Empty;
        var childName = splitIndex >= 0 ? subPath[(splitIndex + 1)..] : subPath;
        using var parent = string.IsNullOrWhiteSpace(parentPath) ? root : root.OpenSubKey(parentPath, writable: true);
        parent?.DeleteSubKeyTree(childName, throwOnMissingSubKey: false);
    }

    private static (RegistryKey Root, string SubPath) OpenRegistryKey(string keyPath)
    {
        const string hkcu = "HKEY_CURRENT_USER\\";
        const string hklm = "HKEY_LOCAL_MACHINE\\";
        const string hkcr = "HKEY_CLASSES_ROOT\\";

        if (keyPath.StartsWith(hkcu, StringComparison.OrdinalIgnoreCase))
        {
            return (Registry.CurrentUser, keyPath[hkcu.Length..]);
        }

        if (keyPath.StartsWith(hklm, StringComparison.OrdinalIgnoreCase))
        {
            return (Registry.LocalMachine, keyPath[hklm.Length..]);
        }

        if (keyPath.StartsWith(hkcr, StringComparison.OrdinalIgnoreCase))
        {
            return (Registry.ClassesRoot, keyPath[hkcr.Length..]);
        }

        return (Registry.CurrentUser, keyPath);
    }
}