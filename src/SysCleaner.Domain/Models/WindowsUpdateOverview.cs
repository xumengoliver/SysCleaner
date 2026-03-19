namespace SysCleaner.Domain.Models;

public sealed class WindowsUpdateOverview
{
    public WindowsUpdateOverview(
        bool pendingReboot,
        DateTime? lastSuccessfulInstallTime,
        string lastSuccessfulInstallTitle,
        IReadOnlyList<WindowsInstalledUpdate> installedUpdates,
        IReadOnlyList<WindowsUpdateEventRecord> recentFailures)
    {
        PendingReboot = pendingReboot;
        LastSuccessfulInstallTime = lastSuccessfulInstallTime;
        LastSuccessfulInstallTitle = lastSuccessfulInstallTitle;
        InstalledUpdates = installedUpdates;
        RecentFailures = recentFailures;
    }

    public bool PendingReboot { get; }
    public DateTime? LastSuccessfulInstallTime { get; }
    public string LastSuccessfulInstallTitle { get; }
    public IReadOnlyList<WindowsInstalledUpdate> InstalledUpdates { get; }
    public IReadOnlyList<WindowsUpdateEventRecord> RecentFailures { get; }
}