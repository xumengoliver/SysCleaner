using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface ISystemRepairService
{
    Task<IReadOnlyList<SystemRepairItem>> AnalyzeAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> RepairIconCacheAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> RepairWindowsAvatarAsync(CancellationToken cancellationToken = default);
}