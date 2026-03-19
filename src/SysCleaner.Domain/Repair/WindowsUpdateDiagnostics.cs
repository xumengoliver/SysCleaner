using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Domain.Repair;

public static class WindowsUpdateDiagnostics
{
    public static IReadOnlyList<SystemRepairItem> BuildItems(
        IReadOnlyList<WindowsUpdateServiceState> services,
        int softwareDistributionCount,
        int downloaderCount,
        bool pendingReboot)
    {
        return
        [
            BuildServiceItem(services),
            BuildCacheItem(softwareDistributionCount, downloaderCount),
            BuildComponentItem(pendingReboot)
        ];
    }

    private static SystemRepairItem BuildServiceItem(IReadOnlyList<WindowsUpdateServiceState> services)
    {
        var missing = services.Count(service => !service.Exists);
        var stopped = services.Count(service => service.Exists && !service.IsRunning);
        var demandStart = services.Count(service => service.Exists && !service.IsAutoStart);

        var health = missing > 0
            ? ItemHealth.Broken
            : stopped > 0 || demandStart > 0
                ? ItemHealth.Review
                : ItemHealth.Healthy;

        var summary = missing > 0
            ? $"检测到 {missing} 个更新核心服务缺失，{stopped} 个未运行。"
            : $"检测到 {stopped} 个服务未运行，{demandStart} 个不是自动启动。";

        return new SystemRepairItem(
            "windows-update-services",
            "更新服务状态",
            "检查 Windows Update、BITS、加密服务和安装模块的状态。",
            summary,
            "可先尝试重启更新核心服务；若服务异常反复出现，再执行更新组件重置。",
            health,
            RequiresExplorerRestart: false,
            RequiresSignOut: false);
    }

    private static SystemRepairItem BuildCacheItem(int softwareDistributionCount, int downloaderCount)
    {
        var total = softwareDistributionCount + downloaderCount;
        var health = total > 2000 ? ItemHealth.Review : ItemHealth.Healthy;
        var summary = $"SoftwareDistribution/Download 中检测到 {softwareDistributionCount} 个文件，Downloader 中检测到 {downloaderCount} 个文件。";

        return new SystemRepairItem(
            "windows-update-cache",
            "更新缓存状态",
            "检查更新下载缓存和 BITS 下载队列，识别缓存堆积、失败任务和异常占用。",
            summary,
            "更新下载长时间卡住、补丁重复失败时，可尝试重置 Windows Update 组件。",
            health,
            RequiresExplorerRestart: false,
            RequiresSignOut: false);
    }

    private static SystemRepairItem BuildComponentItem(bool pendingReboot)
    {
        return new SystemRepairItem(
            "windows-component-store",
            "组件存储与系统文件",
            "检查是否存在待重启更新状态，并提供 DISM 与 SFC 修复入口。",
            pendingReboot ? "检测到系统存在待重启更新状态。" : "未检测到待重启更新标记。",
            "若 Windows Update、应用安装或系统组件持续失败，可运行 DISM RestoreHealth 和 SFC 扫描。",
            pendingReboot ? ItemHealth.Review : ItemHealth.Healthy,
            RequiresExplorerRestart: false,
            RequiresSignOut: pendingReboot);
    }
}