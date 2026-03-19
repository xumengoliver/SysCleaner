using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using SysCleaner.Domain.Repair;

namespace SysCleaner.Tests.Application;

public sealed class WindowsUpdateDiagnosticsTests
{
    [Fact]
    public void BuildItems_MarksServiceItemBroken_WhenCoreServiceMissing()
    {
        var items = WindowsUpdateDiagnostics.BuildItems(
        [
            new WindowsUpdateServiceState("wuauserv", Exists: false, IsRunning: false, IsAutoStart: false),
            new WindowsUpdateServiceState("bits", Exists: true, IsRunning: true, IsAutoStart: true)
        ],
        10,
        0,
        pendingReboot: false);

        Assert.Equal(ItemHealth.Broken, items[0].Health);
        Assert.Contains("缺失", items[0].DetectionSummary);
    }

    [Fact]
    public void BuildItems_MarksComponentItemReview_WhenPendingRebootExists()
    {
        var items = WindowsUpdateDiagnostics.BuildItems(
        [
            new WindowsUpdateServiceState("wuauserv", Exists: true, IsRunning: true, IsAutoStart: true),
            new WindowsUpdateServiceState("bits", Exists: true, IsRunning: true, IsAutoStart: true)
        ],
        0,
        0,
        pendingReboot: true);

        Assert.Equal(ItemHealth.Review, items[2].Health);
        Assert.True(items[2].RequiresSignOut);
    }
}