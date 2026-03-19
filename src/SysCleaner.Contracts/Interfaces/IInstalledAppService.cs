using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IInstalledAppService
{
    Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InstalledApp>> GetBrokenUninstallEntriesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> LaunchUninstallAsync(InstalledApp app, CancellationToken cancellationToken = default);
    Task<OperationResult> RemoveBrokenEntryAsync(InstalledApp app, CancellationToken cancellationToken = default);
}