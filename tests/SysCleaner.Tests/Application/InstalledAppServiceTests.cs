using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using SysCleaner.Infrastructure.Services;

namespace SysCleaner.Tests.Application;

public sealed class InstalledAppServiceTests
{
    [Fact]
    public void DeduplicateInstalledApps_MergesDuplicateEntriesAndKeepsUsableCommand()
    {
        var installDir = Path.Combine(Path.GetTempPath(), "SysCleaner-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installDir);

        var validUninstaller = Path.Combine(installDir, "uninstall.exe");
        File.WriteAllText(validUninstaller, string.Empty);

        try
        {
            var brokenEntry = new InstalledApp(
                "broken",
                "Demo App",
                "Demo Corp",
                "1.0",
                installDir,
                "\"C:\\Missing\\uninstall.exe\" /S",
                string.Empty,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DemoApp-Broken",
                false,
                false,
                ItemHealth.Review,
                string.Empty);

            var validEntry = new InstalledApp(
                "valid",
                "Demo App",
                "Demo Corp",
                "1.0",
                installDir,
                $"\"{validUninstaller}\" /uninstall",
                string.Empty,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\DemoApp-Valid",
                false,
                false,
                ItemHealth.Healthy,
                string.Empty);

            var merged = InstalledAppService.DeduplicateInstalledApps([brokenEntry, validEntry]);

            var app = Assert.Single(merged);
            Assert.Equal(validEntry.Id, app.Id);
            Assert.Equal(validEntry.RegistryPath, app.RegistryPath);
            Assert.Equal(validEntry.UninstallString, app.UninstallString);
            Assert.Equal(ItemHealth.Healthy, app.Health);
        }
        finally
        {
            Directory.Delete(installDir, recursive: true);
        }
    }

    [Fact]
    public void DeduplicateInstalledApps_KeepsEntriesWithDifferentInstallAnchors()
    {
        var apps = InstalledAppService.DeduplicateInstalledApps([
            new InstalledApp(
                "app-1",
                "Shared Name",
                "Vendor",
                "1.0",
                @"C:\Apps\One",
                "\"C:\\Apps\\One\\uninstall.exe\"",
                string.Empty,
                @"HKEY_LOCAL_MACHINE\...\One",
                false,
                false,
                ItemHealth.Review,
                string.Empty),
            new InstalledApp(
                "app-2",
                "Shared Name",
                "Vendor",
                "1.0",
                @"D:\Apps\Two",
                "\"D:\\Apps\\Two\\uninstall.exe\"",
                string.Empty,
                @"HKEY_LOCAL_MACHINE\...\Two",
                false,
                false,
                ItemHealth.Review,
                string.Empty)
        ]);

        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public void ResolvePreferredUninstallCommand_FallsBackToQuietCommand()
    {
        var installDir = Path.Combine(Path.GetTempPath(), "SysCleaner-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installDir);

        var validQuietUninstaller = Path.Combine(installDir, "quiet-uninstall.exe");
        File.WriteAllText(validQuietUninstaller, string.Empty);

        try
        {
            var app = new InstalledApp(
                "app",
                "Quiet Only App",
                "Vendor",
                "1.0",
                installDir,
                "\"C:\\Missing\\uninstall.exe\" /S",
                $"\"{validQuietUninstaller}\" /quiet",
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QuietOnly",
                false,
                false,
                ItemHealth.Review,
                string.Empty);

            var command = InstalledAppService.ResolvePreferredUninstallCommand(app);

            Assert.Equal(app.QuietUninstallString, command);
        }
        finally
        {
            Directory.Delete(installDir, recursive: true);
        }
    }

    [Fact]
    public void InstalledApp_ExposesReadableSourceSummary()
    {
        var machineWide = new InstalledApp(
            "app-1",
            "Shared Name",
            "Vendor",
            "1.0",
            @"C:\Apps\One",
            string.Empty,
            string.Empty,
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\App1",
            false,
            false,
            ItemHealth.Review,
            string.Empty);

        var currentUser = new InstalledApp(
            "app-2",
            "Shared Name",
            string.Empty,
            string.Empty,
            @"C:\Apps\Two",
            string.Empty,
            string.Empty,
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App2",
            false,
            false,
            ItemHealth.Review,
            string.Empty);

        Assert.Equal("所有用户 / 32 位卸载项", machineWide.SourceSummary);
        Assert.Equal("当前用户 / 用户卸载项", currentUser.SourceSummary);
        Assert.Contains("Shared Name", machineWide.DisplayLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledApp_IdentifiesLikelyUninstallableEntries()
    {
        var uninstallable = new InstalledApp(
            "app-1",
            "Shared Name",
            "Vendor",
            "1.0",
            @"C:\Apps\One",
            "msiexec.exe /x {GUID}",
            string.Empty,
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App1",
            false,
            false,
            ItemHealth.Review,
            string.Empty);

        var broken = new InstalledApp(
            "app-2",
            "Ghost App",
            "Vendor",
            "1.0",
            string.Empty,
            string.Empty,
            string.Empty,
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\App2",
            false,
            false,
            ItemHealth.Broken,
            "卸载入口可能已失效");

        Assert.True(uninstallable.HasUninstallCommand);
        Assert.True(uninstallable.IsLikelyUninstallable);
        Assert.False(uninstallable.IsCurrentUserInstall);
        Assert.Equal(1, uninstallable.SourceSortKey);
        Assert.Equal(1, uninstallable.HealthSortKey);
        Assert.False(broken.HasUninstallCommand);
        Assert.False(broken.IsLikelyUninstallable);
        Assert.True(broken.IsCurrentUserInstall);
        Assert.Equal(0, broken.SourceSortKey);
        Assert.Equal(0, broken.HealthSortKey);
    }
}