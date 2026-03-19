using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;
using SysCleaner.Domain.Repair;
using System.Diagnostics;

namespace SysCleaner.Infrastructure.Services;

public sealed class SystemRepairService(IHistoryService historyService) : ISystemRepairService
{
    public Task<IReadOnlyList<SystemRepairItem>> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<SystemRepairItem>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var iconFiles = EnumerateFiles(GetExplorerCacheDirectory(), "iconcache*.db").Count;
            var thumbFiles = EnumerateFiles(GetExplorerCacheDirectory(), "thumbcache*.db").Count;
            var roamingAvatarFiles = EnumerateFiles(GetRoamingAccountPicturesDirectory(), "*.*").Count;
            var cloudAvatarFiles = EnumerateFiles(GetCloudAccountPicturesDirectory(), "*.*").Count;

            return
            [
                SystemRepairDiagnostics.BuildIconRepairItem(iconFiles, thumbFiles),
                SystemRepairDiagnostics.BuildAvatarRepairItem(roamingAvatarFiles, cloudAvatarFiles)
            ];
        }, cancellationToken);
    }

    public async Task<OperationResult> RepairIconCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var explorerCacheDirectory = GetExplorerCacheDirectory();
            var iconFiles = EnumerateFiles(explorerCacheDirectory, "iconcache*.db");
            var thumbFiles = EnumerateFiles(explorerCacheDirectory, "thumbcache*.db");

            RunOptionalProcess("ie4uinit.exe", "-ClearIconCache");
            RestartShellProcesses(["explorer"]);

            var deleted = DeleteFiles(iconFiles) + DeleteFiles(thumbFiles);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            RunOptionalProcess("ie4uinit.exe", "-show");

            var message = deleted == 0
                ? "已执行图标缓存重建流程，并重启资源管理器。"
                : $"已清理 {deleted} 个图标/缩略图缓存文件，并重启资源管理器。";

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "SystemRepair", "RepairIconCache", explorerCacheDirectory, "Success", message), cancellationToken);
            return new OperationResult(true, message);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "SystemRepair", "RepairIconCache", GetExplorerCacheDirectory(), "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> RepairWindowsAvatarAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var avatarDirectories = new[]
            {
                GetRoamingAccountPicturesDirectory(),
                GetCloudAccountPicturesDirectory()
            };

            RestartShellProcesses(["ShellExperienceHost", "StartMenuExperienceHost", "explorer"]);

            var deleted = 0;
            foreach (var directory in avatarDirectories)
            {
                deleted += DeleteFiles(EnumerateFiles(directory, "*.*"));
            }

            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            var message = deleted == 0
                ? "已执行头像缓存重建流程。若头像仍未刷新，建议保持联网后退出并重新登录 Windows 账号。"
                : $"已清理 {deleted} 个头像缓存文件，并重启壳进程。若头像仍未刷新，建议退出并重新登录 Windows 账号。";

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "SystemRepair", "RepairWindowsAvatar", string.Join(";", avatarDirectories), "Success", message), cancellationToken);
            return new OperationResult(true, message);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "SystemRepair", "RepairWindowsAvatar", GetRoamingAccountPicturesDirectory(), "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    private static string GetExplorerCacheDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
    }

    private static string GetRoamingAccountPicturesDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "AccountPictures");
    }

    private static string GetCloudAccountPicturesDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.Windows.CloudExperienceHost_cw5n1h2txyewy", "LocalState", "AccountPictures");
    }

    private static List<string> EnumerateFiles(string directory, string pattern)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static int DeleteFiles(IEnumerable<string> files)
    {
        var deleted = 0;
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch
            {
            }
        }

        return deleted;
    }

    private static void RestartShellProcesses(IEnumerable<string> processNames)
    {
        foreach (var processName in processNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
    }

    private static void RunOptionalProcess(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
        }
        catch
        {
        }
    }
}