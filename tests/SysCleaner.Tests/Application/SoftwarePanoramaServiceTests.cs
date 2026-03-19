using SysCleaner.Application.Services;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Tests.Application;

public sealed class SoftwarePanoramaServiceTests
{
    [Fact]
    public async Task BuildAsync_AggregatesAllMatchedModules()
    {
        var app = new InstalledApp(
            "acme",
            "Acme Cleaner",
            "Acme Labs",
            "2.1",
            @"C:\Program Files\Acme Cleaner",
            @"C:\Program Files\Acme Cleaner\uninstall.exe",
            string.Empty,
            @"HKEY_LOCAL_MACHINE\Software\AcmeCleaner",
            false,
            false,
            ItemHealth.Healthy,
            string.Empty);

        var service = new SoftwarePanoramaService(
            new FakeInstalledAppService(app),
            new FakeResidueService([
                new CleanupCandidate("1", CleanupCategory.ResidualFolder, "Acme Cleaner", @"C:\Users\Oliver\AppData\Roaming\Acme Cleaner", "ResidueScan", "命中常见目录", ItemHealth.Review, RiskLevel.Review, false, true, true, app.Id)
            ]),
            new FakeRegistryService([
                new CleanupCandidate("2", CleanupCategory.RegistryEntry, "AcmeStartup", @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "RegistryRun", "检测到关联值", ItemHealth.Review, RiskLevel.Review, false, true, true, app.Id)
            ]),
            new FakeStartupService([
                new CleanupCandidate("3", CleanupCategory.StartupEntry, "Acme Agent", @"C:\Program Files\Acme Cleaner\agent.exe", "HKCU\\Run", "启动项命中", ItemHealth.Review, RiskLevel.Review, true, true, true),
                new CleanupCandidate("4", CleanupCategory.StartupEntry, "Other App", @"C:\Other\other.exe", "HKCU\\Run", "无关启动项", ItemHealth.Healthy, RiskLevel.Review, true, true, true)
            ]),
            new FakeContextMenuService([
                new CleanupCandidate("5", CleanupCategory.ContextMenuEntry, "Acme Shell", @"C:\Program Files\Acme Cleaner\shell.exe", "*\\shell", "右键菜单命中", ItemHealth.Review, RiskLevel.Review, true, true, true)
            ]),
            new FakeTaskSchedulerService([
                new CleanupCandidate("6", CleanupCategory.ScheduledTask, "Acme Updater", @"C:\Program Files\Acme Cleaner\updater.exe", "\\Acme\\Updater", "计划任务命中", ItemHealth.Review, RiskLevel.Review, true, true, true)
            ]),
            new FakeServiceControlService([
                new CleanupCandidate("7", CleanupCategory.Service, "Acme Service", @"C:\Program Files\Acme Cleaner\service.exe", "服务名：AcmeSvc", "服务项命中", ItemHealth.Review, RiskLevel.Review, false, true, false, app.Id),
                new CleanupCandidate("8", CleanupCategory.Service, "Other Service", @"C:\Other\svc.exe", "服务名：OtherSvc", "无关服务", ItemHealth.Healthy, RiskLevel.Review, false, true, false)
            ]));

        var snapshot = await service.BuildAsync(app.Id);

        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Residues);
        Assert.Single(snapshot.RegistryEntries);
        Assert.Single(snapshot.StartupItems);
        Assert.Single(snapshot.ContextMenuItems);
        Assert.Single(snapshot.ScheduledTasks);
        Assert.Single(snapshot.Services);
        Assert.Equal(6, snapshot.AllItems.Count);
    }

    [Fact]
    public async Task BuildAsync_ReturnsNull_WhenAppDoesNotExist()
    {
        var service = new SoftwarePanoramaService(
            new FakeInstalledAppService(),
            new FakeResidueService([]),
            new FakeRegistryService([]),
            new FakeStartupService([]),
            new FakeContextMenuService([]),
            new FakeTaskSchedulerService([]),
            new FakeServiceControlService([]));

        var snapshot = await service.BuildAsync("missing");

        Assert.Null(snapshot);
    }

    private sealed class FakeInstalledAppService(params InstalledApp[] apps) : IInstalledAppService
    {
        public Task<IReadOnlyList<InstalledApp>> GetInstalledAppsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InstalledApp>>(apps);

        public Task<IReadOnlyList<InstalledApp>> GetBrokenUninstallEntriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task<OperationResult> LaunchUninstallAsync(InstalledApp app, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> RemoveBrokenEntryAsync(InstalledApp app, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeResidueService(IReadOnlyList<CleanupCandidate> items) : IResidueAnalysisService
    {
        public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(InstalledApp app, CancellationToken cancellationToken = default) => Task.FromResult(items);
    }

    private sealed class FakeRegistryService(IReadOnlyList<CleanupCandidate> items) : IRegistryCleanupService
    {
        public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(InstalledApp app, CancellationToken cancellationToken = default) => Task.FromResult(items);

        public Task<IReadOnlyList<CleanupCandidate>> ScanBrokenEntriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CleanupCandidate>>([]);

        public Task<IReadOnlyList<RegistrySearchResult>> SearchAsync(RegistrySearchOptions options, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RegistrySearchResult>>([]);

        public Task<OperationResult> DeleteSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> UpdateSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, string newValue, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> ReplaceInSearchResultsAsync(IReadOnlyList<RegistrySearchResult> results, RegistryReplaceOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeStartupService(IReadOnlyList<CleanupCandidate> items) : IStartupItemService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetStartupItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(items);

        public Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeContextMenuService(IReadOnlyList<CleanupCandidate> items) : IContextMenuService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetEntriesAsync(CancellationToken cancellationToken = default) => Task.FromResult(items);

        public Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeTaskSchedulerService(IReadOnlyList<CleanupCandidate> items) : ITaskSchedulerService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult(items);

        public Task<OperationResult> DisableAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }

    private sealed class FakeServiceControlService(IReadOnlyList<CleanupCandidate> items) : IServiceControlService
    {
        public Task<IReadOnlyList<CleanupCandidate>> GetServicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(items);

        public Task<OperationResult> StopAsync(string serviceName, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));

        public Task<OperationResult> DeleteAsync(CleanupCandidate candidate, CancellationToken cancellationToken = default) => Task.FromResult(new OperationResult(true, "ok"));
    }
}