using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Repair;

namespace SysCleaner.Tests.Application;

public sealed class SystemRepairDiagnosticsTests
{
    [Fact]
    public void BuildIconRepairItem_UsesReviewStatus_WhenNoCacheFilesExist()
    {
        var item = SystemRepairDiagnostics.BuildIconRepairItem(0, 0);

        Assert.Equal(ItemHealth.Review, item.Health);
        Assert.Contains("未发现本地图标缓存数据库", item.DetectionSummary);
        Assert.True(item.RequiresExplorerRestart);
    }

    [Fact]
    public void BuildAvatarRepairItem_ReportsCacheCounts_WhenCacheFilesExist()
    {
        var item = SystemRepairDiagnostics.BuildAvatarRepairItem(2, 3);

        Assert.Equal(ItemHealth.Healthy, item.Health);
        Assert.Contains("2 个漫游头像缓存", item.DetectionSummary);
        Assert.Contains("3 个 CloudExperienceHost 头像缓存", item.DetectionSummary);
        Assert.True(item.RequiresSignOut);
    }
}