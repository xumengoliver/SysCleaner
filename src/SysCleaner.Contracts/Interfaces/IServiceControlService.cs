using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IServiceControlService
{
    Task<IReadOnlyList<CleanupCandidate>> GetServicesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default);
}