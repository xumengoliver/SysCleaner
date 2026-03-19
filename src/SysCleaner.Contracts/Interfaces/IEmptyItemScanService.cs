using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IEmptyItemScanService
{
    Task<IReadOnlyList<CleanupCandidate>> ScanAsync(string rootPath, bool includeSubfolders = true, int maxResults = int.MaxValue, IProgress<EmptyItemScanProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanupCandidate>> ExecuteAsync(string rootPath, IReadOnlyList<CleanupCandidate> selectedCandidates, CancellationToken cancellationToken = default);
}