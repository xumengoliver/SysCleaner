using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IEmptyItemScanService
{
    Task<IReadOnlyList<CleanupCandidate>> ScanAsync(string rootPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanupCandidate>> ExecuteAsync(string rootPath, IReadOnlyList<CleanupCandidate> selectedCandidates, CancellationToken cancellationToken = default);
}