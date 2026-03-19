namespace SysCleaner.Domain.Enums;

public enum CleanupCategory
{
    InstalledApplication,
    BrokenUninstallEntry,
    ResidualFile,
    ResidualFolder,
    RegistryEntry,
    StartupEntry,
    ContextMenuEntry,
    LockedResource,
    EmptyFile,
    EmptyFolder,
    Shortcut,
    ScheduledTask,
    Service
}