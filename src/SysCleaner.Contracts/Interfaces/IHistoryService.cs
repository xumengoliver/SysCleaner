using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface IHistoryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task LogAsync(OperationLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationLogEntry>> GetRecentAsync(int take = 200, CancellationToken cancellationToken = default);
}