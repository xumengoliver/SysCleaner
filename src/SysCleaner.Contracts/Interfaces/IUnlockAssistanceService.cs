using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IUnlockAssistanceService
{
    Task<OperationResult> CloseProcessAsync(LockInfo lockInfo, CancellationToken cancellationToken = default);
    Task<OperationResult> ForceDeleteAsync(string targetPath, CancellationToken cancellationToken = default);
    Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ScheduleDeleteOnRebootAsync(string targetPath, CancellationToken cancellationToken = default);
    Task<OperationResult> StopServiceAsync(LockInfo lockInfo, CancellationToken cancellationToken = default);
}