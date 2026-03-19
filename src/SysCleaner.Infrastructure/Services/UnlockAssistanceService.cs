using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using System.Diagnostics;

namespace SysCleaner.Infrastructure.Services;

public sealed class UnlockAssistanceService(IHistoryService historyService, IServiceControlService serviceControlService) : IUnlockAssistanceService
{
    public async Task<OperationResult> CloseProcessAsync(LockInfo lockInfo, CancellationToken cancellationToken = default)
    {
        if (!lockInfo.CanTerminate || lockInfo.Risk == RiskLevel.Protected)
        {
            return new OperationResult(false, "该占用项不允许强制关闭。");
        }

        try
        {
            using var process = Process.GetProcessById(lockInfo.ProcessId);
            var usedGracefulClose = false;
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    usedGracefulClose = process.CloseMainWindow();
                    if (usedGracefulClose)
                    {
                        var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(3), cancellationToken);
                        if (!exited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    else
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            else
            {
                process.Kill(entireProcessTree: true);
            }

            var message = usedGracefulClose ? "已尝试优雅关闭占用进程，必要时已升级为强制结束。" : "已尝试结束占用进程。";
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "CloseProcess", lockInfo.HolderName, "Success", message), cancellationToken);
            return new OperationResult(true, message);
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "CloseProcess", lockInfo.HolderName, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> ForceDeleteAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        var result = await PathDeletionHelper.ForceDeleteAsync(targetPath, recursive: true, cancellationToken);
        await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "ForceDelete", targetPath, result.Success ? "Success" : "Failed", result.Message), cancellationToken);
        return new OperationResult(result.Success, result.Message);
    }

    public async Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                process.Kill();
            }

            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "RestartExplorer", "explorer.exe", "Success", string.Empty), cancellationToken);
            return new OperationResult(true, "已重启资源管理器。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "RestartExplorer", "explorer.exe", "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public async Task<OperationResult> ScheduleDeleteOnRebootAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await PathDeletionHelper.ScheduleDeleteOnRebootAsync(targetPath);
            if (!result.Success)
            {
                return result;
            }

            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "ScheduleDeleteOnReboot", targetPath, "Success", string.Empty), cancellationToken);
            return new OperationResult(true, "已安排重启后删除。");
        }
        catch (Exception ex)
        {
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "ScheduleDeleteOnReboot", targetPath, "Failed", ex.Message), cancellationToken);
            return new OperationResult(false, ex.Message);
        }
    }

    public Task<OperationResult> StopServiceAsync(LockInfo lockInfo, CancellationToken cancellationToken = default)
    {
        if (!lockInfo.CanStopService || lockInfo.Risk == RiskLevel.Protected)
        {
            return Task.FromResult(new OperationResult(false, "该占用项不允许直接停止服务。"));
        }

        var serviceName = ResolveServiceName(lockInfo);
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Task.FromResult(new OperationResult(false, "未识别到服务名，无法停止服务。"));
        }

        return serviceControlService.StopAsync(serviceName, cancellationToken);
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waitTask = process.WaitForExitAsync(linkedCts.Token);
        var delayTask = Task.Delay(timeout, linkedCts.Token);
        var completedTask = await Task.WhenAny(waitTask, delayTask);
        if (completedTask == waitTask)
        {
            linkedCts.Cancel();
            await waitTask;
            return true;
        }

        return process.HasExited;
    }

    private static string ResolveServiceName(LockInfo lockInfo)
    {
        if (!string.IsNullOrWhiteSpace(lockInfo.Notes))
        {
            return lockInfo.Notes.Trim();
        }

        return string.Empty;
    }
}