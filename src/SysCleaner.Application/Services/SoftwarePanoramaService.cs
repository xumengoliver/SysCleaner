using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Matching;
using SysCleaner.Domain.Models;

namespace SysCleaner.Application.Services;

public sealed class SoftwarePanoramaService(
    IInstalledAppService installedAppService,
    IResidueAnalysisService residueAnalysisService,
    IRegistryCleanupService registryCleanupService,
    IStartupItemService startupItemService,
    IContextMenuService contextMenuService,
    ITaskSchedulerService taskSchedulerService,
    IServiceControlService serviceControlService)
{
    public async Task<SoftwarePanoramaSnapshot?> BuildAsync(string appId, CancellationToken cancellationToken = default)
    {
        var app = (await installedAppService.GetInstalledAppsAsync(cancellationToken))
            .FirstOrDefault(item => item.Id.Equals(appId, StringComparison.OrdinalIgnoreCase));

        if (app is null)
        {
            return null;
        }

        var residueTask = residueAnalysisService.ScanAsync(app, cancellationToken);
        var registryTask = registryCleanupService.ScanAsync(app, cancellationToken);
        var startupTask = startupItemService.GetStartupItemsAsync(cancellationToken);
        var contextMenuTask = contextMenuService.GetEntriesAsync(cancellationToken);
        var scheduledTaskTask = taskSchedulerService.GetTasksAsync(cancellationToken);
        var serviceTask = serviceControlService.GetServicesAsync(cancellationToken);

        await Task.WhenAll(residueTask, registryTask, startupTask, contextMenuTask, scheduledTaskTask, serviceTask);

        return new SoftwarePanoramaSnapshot(
            app,
            residueTask.Result,
            registryTask.Result,
            FilterByApp(app, startupTask.Result),
            FilterByApp(app, contextMenuTask.Result),
                FilterByApp(app, scheduledTaskTask.Result),
                FilterByApp(app, serviceTask.Result));
    }

    private static IReadOnlyList<CleanupCandidate> FilterByApp(InstalledApp app, IReadOnlyList<CleanupCandidate> candidates)
    {
        return candidates
            .Where(candidate => MatchesApp(app, candidate))
            .OrderByDescending(candidate => candidate.Health)
            .ThenBy(candidate => candidate.Title)
            .ToList();
    }

    private static bool MatchesApp(InstalledApp app, CleanupCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.RelatedAppId)
            && candidate.RelatedAppId.Equals(app.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return InstalledAppMatcher.Matches(
            app,
            candidate.Title,
            candidate.TargetPath,
            candidate.Source,
            candidate.Evidence,
            candidate.Metadata);
    }
}