using SysCleaner.Domain.Models;

namespace SysCleaner.Contracts.Interfaces;

public interface ILockDetectionService
{
    Task<IReadOnlyList<LockInfo>> DetectLocksAsync(string targetPath, CancellationToken cancellationToken = default);
}