using SysCleaner.Contracts.Models;
using SysCleaner.Infrastructure.Interop;
using System.Diagnostics;

namespace SysCleaner.Infrastructure.Services;

internal enum PathDeletionStatus
{
    Deleted,
    ForceDeleted,
    ScheduledOnReboot,
    Missing,
    Failed
}

internal sealed record PathDeletionResult(PathDeletionStatus Status, string Message)
{
    public bool Success => Status is PathDeletionStatus.Deleted or PathDeletionStatus.ForceDeleted or PathDeletionStatus.ScheduledOnReboot;
}

internal static class PathDeletionHelper
{
    private const string AdministratorsSid = "*S-1-5-32-544";
    private const int MoveFileDelayUntilReboot = 0x4;

    public static async Task<PathDeletionResult> DeleteAsync(string targetPath, bool recursive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new PathDeletionResult(PathDeletionStatus.Failed, "目标路径不能为空。");
        }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return new PathDeletionResult(PathDeletionStatus.Missing, "目标不存在。");
        }

        try
        {
            DeletePath(targetPath, recursive);
            return new PathDeletionResult(PathDeletionStatus.Deleted, "删除完成。");
        }
        catch (Exception ex) when (IsRetryableDeletionException(ex))
        {
            try
            {
                await PreparePathForDeletionAsync(targetPath, cancellationToken);
                DeletePath(targetPath, recursive);
                return new PathDeletionResult(PathDeletionStatus.ForceDeleted, "删除完成。已自动接管所有权并调整权限。");
            }
            catch (Exception forceDeleteException) when (IsRetryableDeletionException(forceDeleteException))
            {
                if (TryScheduleDeleteOnReboot(targetPath))
                {
                    return new PathDeletionResult(PathDeletionStatus.ScheduledOnReboot, "目标当前无法立即删除，已自动安排为重启后删除。");
                }

                return new PathDeletionResult(PathDeletionStatus.Failed, BuildFailureMessage(forceDeleteException));
            }
            catch (Exception forceDeleteException)
            {
                return new PathDeletionResult(PathDeletionStatus.Failed, BuildFailureMessage(forceDeleteException));
            }
        }
        catch (Exception ex)
        {
            return new PathDeletionResult(PathDeletionStatus.Failed, BuildFailureMessage(ex));
        }
    }

    public static async Task<PathDeletionResult> ForceDeleteAsync(string targetPath, bool recursive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new PathDeletionResult(PathDeletionStatus.Failed, "目标路径不能为空。");
        }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return new PathDeletionResult(PathDeletionStatus.Missing, "目标不存在。");
        }

        try
        {
            await PreparePathForDeletionAsync(targetPath, cancellationToken);
            DeletePath(targetPath, recursive);
            return new PathDeletionResult(PathDeletionStatus.ForceDeleted, "已强制删除目标；如果资源管理器仍显示该项，请刷新当前目录。");
        }
        catch (Exception ex) when (IsRetryableDeletionException(ex))
        {
            if (TryScheduleDeleteOnReboot(targetPath))
            {
                return new PathDeletionResult(PathDeletionStatus.ScheduledOnReboot, "目标仍被占用，已自动安排为重启后删除。");
            }

            return new PathDeletionResult(PathDeletionStatus.Failed, BuildFailureMessage(ex));
        }
        catch (Exception ex)
        {
            return new PathDeletionResult(PathDeletionStatus.Failed, BuildFailureMessage(ex));
        }
    }

    public static Task<OperationResult> ScheduleDeleteOnRebootAsync(string targetPath)
    {
        return Task.FromResult(TryScheduleDeleteOnReboot(targetPath)
            ? new OperationResult(true, "已安排重启后删除。")
            : new OperationResult(false, "无法安排重启后删除。"));
    }

    private static async Task PreparePathForDeletionAsync(string targetPath, CancellationToken cancellationToken)
    {
        var isDirectory = Directory.Exists(targetPath);

        await RunBestEffortProcessAsync(
            "takeown.exe",
            isDirectory
                ? ["/f", targetPath, "/a", "/r", "/d", "Y"]
                : ["/f", targetPath, "/a", "/d", "Y"],
            cancellationToken);

        await RunBestEffortProcessAsync(
            "icacls.exe",
            isDirectory
                ? [targetPath, "/grant", $"{AdministratorsSid}:F", "/t", "/c"]
                : [targetPath, "/grant", $"{AdministratorsSid}:F", "/c"],
            cancellationToken);

        NormalizeAttributes(targetPath);
    }

    private static void DeletePath(string targetPath, bool recursive)
    {
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
            return;
        }

        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, recursive);
        }
    }

    private static bool TryScheduleDeleteOnReboot(string targetPath)
    {
        return RestartManagerInterop.MoveFileEx(targetPath, null, MoveFileDelayUntilReboot);
    }

    private static void NormalizeAttributes(string targetPath)
    {
        if (File.Exists(targetPath))
        {
            try
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
            }
            catch
            {
            }

            return;
        }

        if (!Directory.Exists(targetPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(targetPath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        }))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch
            {
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(targetPath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        }).OrderByDescending(path => path.Length))
        {
            try
            {
                new DirectoryInfo(directory).Attributes = FileAttributes.Directory;
            }
            catch
            {
            }
        }

        try
        {
            new DirectoryInfo(targetPath).Attributes = FileAttributes.Directory;
        }
        catch
        {
        }
    }

    private static async Task RunBestEffortProcessAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
    }

    private static bool IsRetryableDeletionException(Exception exception)
    {
        return exception is UnauthorizedAccessException or IOException;
    }

    private static string BuildFailureMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "删除已取消。",
            UnauthorizedAccessException => "已尝试接管所有权并授予管理员权限，但仍没有足够权限删除该目标。",
            IOException => "目标可能仍被占用，且未能安排重启后删除。",
            _ => exception.Message
        };
    }
}