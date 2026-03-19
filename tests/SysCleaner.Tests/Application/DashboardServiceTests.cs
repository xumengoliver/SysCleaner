using SysCleaner.Application.Services;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Tests.Application;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task BuildSummaryAsync_IncludesSystemServiceModule()
    {
        var service = new DashboardService(
            new FakeInstalledAppService(),
            new FakeStartupService(),
            new FakeContextMenuService(),
            new FakeTaskSchedulerService(),
            new FakeServiceControlService(),
            new FakeHistoryService());

        var summary = await service.BuildSummaryAsync();

        var module = summary.Single(item => item.RouteKey == "service");
        Assert.Equal("系统服务", module.Title);
        Assert.Equal(2, module.Count);
    }

    [Fact]
    public async Task BuildSnapshotAsync_ReturnsRecommendationsAndRecentEntries()
    {
        var service = new DashboardService(
            new FakeInstalledAppService(),
            new FakeStartupService(),
            new FakeContextMenuService(),
            new FakeTaskSchedulerService(),
            new FakeServiceControlService(),
            new FakeHistoryService());

        var snapshot = await service.BuildSnapshotAsync();

        Assert.Equal(6, snapshot.SummaryItems.Count);
        Assert.NotEmpty(snapshot.Recommendations);
        Assert.NotEmpty(snapshot.RecentEntries);
        Assert.Equal(6, snapshot.TotalIssueCount);
        Assert.Equal(1, snapshot.RecentActionCount);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Recommendations[0].PriorityLabel));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Recommendations[0].ReasonLabel));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Recommendations[0].StrategyLabel));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Recommendations[0].EntryLabel));
    }

    [Fact]
    public async Task BuildSnapshotAsync_PrioritizesModulesWithFailuresOrHigherRisk()
    {
        var service = new DashboardService(
            new FakeInstalledAppService(),
            new FakeStartupService(),
            new FakeContextMenuService(),
            new FakeTaskSchedulerService(),
            new FakeServiceControlService(),
            new FakeHistoryServiceWithFailure());

        var snapshot = await service.BuildSnapshotAsync();

        Assert.StartsWith("优先复核：系统服务", snapshot.Recommendations[0].Title);
        Assert.Contains("失败动作", snapshot.Recommendations[0].Detail);
        Assert.Equal("连续失败", snapshot.Recommendations[0].PriorityLabel);
        Assert.Equal("最近连续失败 2 次", snapshot.Recommendations[0].ReasonLabel);
        Assert.Equal("暂停重复执行，先定位失败根因", snapshot.Recommendations[0].StrategyLabel);
        Assert.Equal("建议入口：系统服务", snapshot.Recommendations[0].EntryLabel);
        Assert.True(snapshot.Recommendations[0].PauseRepeatedExecution);
        Assert.True(snapshot.Recommendations[0].RequiresManualConfirmation);
    }

    private sealed class FakeInstalledAppService : IInstalledAppService
    {
        public Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task<IReadOnlyList<InstalledApp>> GetBrokenUninstallEntriesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InstalledApp>>([
                new InstalledApp("broken", "Broken App", "Vendor", "1.0", string.Empty, string.Empty, string.Empty, @"HKEY_LOCAL_MACHINE\Software\Broken", false, false, ItemHealth.Broken, "失效")
            ]);

        public Task<OperationResult> LaunchUninstallAsync(InstalledApp app, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> RemoveBrokenEntryAsync(InstalledApp app, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeStartupService : IStartupItemService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetStartupItemsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CleanupCandidate>>([
                new CleanupCandidate("startup", CleanupCategory.StartupEntry, "Startup", @"C:\App\app.exe", "HKCU\\Run", "ok", ItemHealth.Healthy, RiskLevel.Review, true, true, true)
            ]);

        public Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeContextMenuService : IContextMenuService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetEntriesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CleanupCandidate>>([
                new CleanupCandidate("ctx", CleanupCategory.ContextMenuEntry, "Menu", @"C:\App\shell.exe", "*\\shell", "ok", ItemHealth.Healthy, RiskLevel.Review, true, true, true)
            ]);

        public Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeTaskSchedulerService : ITaskSchedulerService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetTasksAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CleanupCandidate>>([
                new CleanupCandidate("task", CleanupCategory.ScheduledTask, "Task", @"C:\App\task.exe", "\\App\\Task", "ok", ItemHealth.Healthy, RiskLevel.Review, true, true, true)
            ]);

        public Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeServiceControlService : IServiceControlService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetServicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CleanupCandidate>>([
                new CleanupCandidate("svc-1", CleanupCategory.Service, "Updater Service", @"C:\Program Files\Vendor\svc.exe", "服务名：VendorSvc", "ok", ItemHealth.Review, RiskLevel.Review, false, true, false),
                new CleanupCandidate("svc-2", CleanupCategory.Service, "残留服务", @"C:\Missing\svc.exe", "服务名：GhostSvc", "目标缺失", ItemHealth.Broken, RiskLevel.Review, false, true, false)
            ]);

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeHistoryService : IHistoryService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogAsync(OperationLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OperationLogEntry>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OperationLogEntry>>([
                new OperationLogEntry(1, DateTime.Now, "Service", "Delete", "VendorSvc", "Success", "ok")
            ]);
    }

    private sealed class FakeHistoryServiceWithFailure : IHistoryService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogAsync(OperationLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OperationLogEntry>> GetRecentAsync(int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OperationLogEntry>>([
                new OperationLogEntry(1, DateTime.Now, "Service", "Delete", "VendorSvc", "Failed", "access denied"),
                new OperationLogEntry(2, DateTime.Now.AddMinutes(-1), "Service", "Delete", "GhostSvc", "Failed", "service marked for deletion"),
                new OperationLogEntry(3, DateTime.Now.AddMinutes(-2), "Startup", "Disable", "App", "Success", "ok")
            ]);
    }
}