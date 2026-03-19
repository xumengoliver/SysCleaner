namespace SysCleaner.Domain.Models;

public sealed class SoftwarePanoramaSnapshot
{
    public SoftwarePanoramaSnapshot(
        InstalledApp app,
        IReadOnlyList<CleanupCandidate> residues,
        IReadOnlyList<CleanupCandidate> registryEntries,
        IReadOnlyList<CleanupCandidate> startupItems,
        IReadOnlyList<CleanupCandidate> contextMenuItems,
        IReadOnlyList<CleanupCandidate> scheduledTasks,
        IReadOnlyList<CleanupCandidate> services)
    {
        App = app;
        Residues = residues;
        RegistryEntries = registryEntries;
        StartupItems = startupItems;
        ContextMenuItems = contextMenuItems;
        ScheduledTasks = scheduledTasks;
        Services = services;
    }

    public InstalledApp App { get; }
    public IReadOnlyList<CleanupCandidate> Residues { get; }
    public IReadOnlyList<CleanupCandidate> RegistryEntries { get; }
    public IReadOnlyList<CleanupCandidate> StartupItems { get; }
    public IReadOnlyList<CleanupCandidate> ContextMenuItems { get; }
    public IReadOnlyList<CleanupCandidate> ScheduledTasks { get; }
    public IReadOnlyList<CleanupCandidate> Services { get; }

    public IReadOnlyList<CleanupCandidate> AllItems =>
    [
        ..Residues,
        ..RegistryEntries,
        ..StartupItems,
        ..ContextMenuItems,
        ..ScheduledTasks,
        ..Services
    ];
}