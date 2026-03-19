using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using SysCleaner.Infrastructure.Services;

namespace SysCleaner.Tests.Application;

public sealed class UnlockAssistanceServiceTests
{
    [Fact]
    public async Task StopServiceAsync_UsesServiceShortNameFromLockInfo()
    {
        var serviceControl = new FakeServiceControlService();
        var service = new UnlockAssistanceService(new FakeHistoryService(), serviceControl);
        var lockInfo = new LockInfo(
            "lock-1",
            @"C:\Temp\locked.log",
            "Vendor Service",
            42,
            @"C:\Program Files\Vendor\vendor.exe",
            string.Empty,
            LockKind.Service,
            RiskLevel.High,
            false,
            true,
            "建议先停止服务后再复检。",
            "VendorSvc");

        var result = await service.StopServiceAsync(lockInfo);

        Assert.True(result.Success);
        Assert.Equal("VendorSvc", serviceControl.LastStoppedServiceName);
    }

    [Fact]
    public async Task StopServiceAsync_ReturnsFailureWhenServiceNameIsMissing()
    {
        var service = new UnlockAssistanceService(new FakeHistoryService(), new FakeServiceControlService());
        var lockInfo = new LockInfo(
            "lock-2",
            @"C:\Temp\locked.log",
            "Vendor Service",
            42,
            @"C:\Program Files\Vendor\vendor.exe",
            string.Empty,
            LockKind.Service,
            RiskLevel.High,
            false,
            true,
            "建议先停止服务后再复检。",
            string.Empty);

        var result = await service.StopServiceAsync(lockInfo);

        Assert.False(result.Success);
        Assert.Contains("未识别到服务名", result.Message);
    }

    private sealed class FakeHistoryService : IHistoryService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogAsync(OperationLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OperationLogEntry>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OperationLogEntry>>([]);
    }

    private sealed class FakeServiceControlService : IServiceControlService
    {
        public string? LastStoppedServiceName { get; private set; }

        public Task<IReadOnlyList<CleanupCandidate>> GetServicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CleanupCandidate>>([]);

        public Task<OperationResult> StopAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            LastStoppedServiceName = serviceName;
            return Task.FromResult(new OperationResult(true, "已停止服务。"));
        }

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new OperationResult(true, "ok"));
    }
}