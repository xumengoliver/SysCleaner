using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IWindowsUpdateRepairService
{
    Task<IReadOnlyList<SystemRepairItem>> AnalyzeAsync(CancellationToken cancellationToken = default);
    Task<WindowsUpdateOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> UninstallUpdateAsync(WindowsInstalledUpdate update, CancellationToken cancellationToken = default);
    Task<OperationResult> RestartCoreServicesAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ResetWindowsUpdateComponentsAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> RunDismRestoreHealthAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> RunSfcScanAsync(CancellationToken cancellationToken = default);
}