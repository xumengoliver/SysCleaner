using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IResidueAnalysisService
{
    Task<IReadOnlyList<CleanupCandidate>> ScanAsync(InstalledApp app, CancellationToken cancellationToken = default);
}