using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface ITaskSchedulerService
{
    Task<IReadOnlyList<CleanupCandidate>> GetTasksAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default);
}