using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using System.Diagnostics;

namespace SysCleaner.Infrastructure.Services;

public sealed class UnlockAssistanceService(IHistoryService historyService) : IUnlockAssistanceService
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
            process.Kill(entireProcessTree: true);
            await historyService.LogAsync(new OperationLogEntry(0, DateTime.Now, "Unlock", "CloseProcess", lockInfo.HolderName, "Success", lockInfo.HolderPath), cancellationToken);
            return new OperationResult(true, "已尝试关闭占用进程。");
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
}