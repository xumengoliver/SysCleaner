using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IRegistryCleanupService
{
    Task<IReadOnlyList<CleanupCandidate>> ScanAsync(InstalledApp app, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanupCandidate>> ScanBrokenEntriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RegistrySearchResult>> SearchAsync(RegistrySearchOptions options, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, string newValue, CancellationToken cancellationToken = default);
    Task<OperationResult> ReplaceInSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, RegistryReplaceOptions options, CancellationToken cancellationToken = default);
}