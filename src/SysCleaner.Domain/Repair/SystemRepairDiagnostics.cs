using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Domain.Repair;

public static class SystemRepairDiagnostics
{
    public static SystemRepairItem BuildIconRepairItem(int iconCacheCount, int thumbCacheCount)
    {
        var total = iconCacheCount + thumbCacheCount;
        var summary = total == 0
            ? "未发现本地图标缓存数据库，图标显示异常通常可通过重建缓存恢复。"
            : $"检测到 {iconCacheCount} 个图标缓存文件，{thumbCacheCount} 个缩略图缓存文件。";

        return new SystemRepairItem(
            "icon-cache",
            "应用图标修复",
            "重建资源管理器图标缓存与缩略图缓存，修复图标空白、错误关联和旧图标残留。",
            summary,
            "执行后会重启 Explorer，并清理本地 iconcache/thumbcache。",
            total == 0 ? ItemHealth.Review : ItemHealth.Healthy,
            RequiresExplorerRestart: true,
            RequiresSignOut: false);
    }

    public static SystemRepairItem BuildAvatarRepairItem(int roamingPictureCount, int cloudPictureCount)
    {
        var total = roamingPictureCount + cloudPictureCount;
        var summary = total == 0
            ? "当前用户目录下未发现账号头像缓存，若已登录 Microsoft 账号但头像仍未刷新，可尝试重建头像缓存。"
            : $"检测到 {roamingPictureCount} 个漫游头像缓存，{cloudPictureCount} 个 CloudExperienceHost 头像缓存。";

        return new SystemRepairItem(
            "windows-avatar",
            "Windows 账号头像修复",
            "清理当前用户头像缓存并重启壳进程，促使系统重新拉取 Microsoft 账号头像。",
            summary,
            "建议在联网状态下执行；若执行后头像仍未更新，可退出并重新登录 Windows 账号。",
            total == 0 ? ItemHealth.Review : ItemHealth.Healthy,
            RequiresExplorerRestart: true,
            RequiresSignOut: true);
    }
}