using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface ICleanupExecutionService
{
    Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default);
}