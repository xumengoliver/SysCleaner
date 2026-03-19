using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;

namespace SysCleaner.Application.Services;

public sealed class DashboardService(
    IInstalledAppService installedAppService,
    IStartupItemService startupItemService,
    IContextMenuService contextMenuService,
    ITaskSchedulerService taskSchedulerService,
    IServiceControlService serviceControlService,
    IHistoryService historyService)
{
    public async Task<DashboardSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var apps = await installedAppService.GetBrokenUninstallEntriesAsync(cancellationToken);
        var startup = await startupItemService.GetStartupItemsAsync(cancellationToken);
        var contextMenus = await contextMenuService.GetEntriesAsync(cancellationToken);
        var scheduledTasks = await taskSchedulerService.GetTasksAsync(cancellationToken);
        var services = await serviceControlService.GetServicesAsync(cancellationToken);
        var history = await historyService.GetRecentAsync(8, cancellationToken);

        var summaryItems = new List<ModuleSummary>
        {
            new("失效卸载条目", "卸载列表中已损坏或失效的项目", apps.Count, "#D97706", "broken-uninstall"),
            new("启动项", "开机启动项、失效项与高影响项", startup.Count, "#0F766E", "startup"),
            new("右键菜单", "命令型菜单与 Shell 处理器", contextMenus.Count, "#2563EB", "context-menu"),
            new("计划任务", "第三方计划任务、失效项与高影响项", scheduledTasks.Count, "#0F172A", "scheduled-task"),
            new("系统服务", "第三方服务与残留服务项", services.Count, "#B45309", "service"),
            new("最近操作", "已执行清理与治理动作", history.Count, "#7C3AED", "history")
        };

        var totalIssueCount = summaryItems
            .Where(item => item.RouteKey != "history")
            .Sum(item => item.Count);

        var failureCount = history.Count(entry => string.Equals(entry.Result, "Failed", StringComparison.OrdinalIgnoreCase));

        var recommendationInputs = new[]
        {
            BuildRecommendationInput(
                summaryItems[0],
                apps.Count(item => item.Health is ItemHealth.Broken or ItemHealth.Review),
                apps.Count(item => item.Health == ItemHealth.Broken),
                CountFailures(history, "BrokenUninstall"),
                CountConsecutiveFailures(history, "BrokenUninstall")),
            BuildRecommendationInput(
                summaryItems[1],
                startup.Count(item => item.Risk is RiskLevel.High or RiskLevel.Protected || item.Health is ItemHealth.Broken or ItemHealth.Review),
                startup.Count(item => item.Health == ItemHealth.Broken),
                CountFailures(history, "Startup"),
                CountConsecutiveFailures(history, "Startup")),
            BuildRecommendationInput(
                summaryItems[2],
                contextMenus.Count(item => item.Risk is RiskLevel.High or RiskLevel.Protected || item.Health is ItemHealth.Broken or ItemHealth.Review),
                contextMenus.Count(item => item.Health == ItemHealth.Broken),
                CountFailures(history, "ContextMenu"),
                CountConsecutiveFailures(history, "ContextMenu")),
            BuildRecommendationInput(
                summaryItems[3],
                scheduledTasks.Count(item => item.Risk is RiskLevel.High or RiskLevel.Protected || item.Health is ItemHealth.Broken or ItemHealth.Review),
                scheduledTasks.Count(item => item.Health == ItemHealth.Broken),
                CountFailures(history, "ScheduledTask"),
                CountConsecutiveFailures(history, "ScheduledTask")),
            BuildRecommendationInput(
                summaryItems[4],
                services.Count(item => item.Risk is RiskLevel.High or RiskLevel.Protected || item.Health is ItemHealth.Broken or ItemHealth.Review),
                services.Count(item => item.Health == ItemHealth.Broken),
                CountFailures(history, "Service"),
                CountConsecutiveFailures(history, "Service"))
        };

        var recommendations = recommendationInputs
            .Where(item => item.Summary.Count > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.BrokenCount)
            .ThenByDescending(item => item.Summary.Count)
            .Take(3)
            .Select(item => new DashboardRecommendation(
                item.ConsecutiveFailureCount > 1 || item.FailureCount > 0 ? $"优先复核：{item.Summary.Title}" : $"优先处理：{item.Summary.Title}",
                BuildRecommendationDetail(item),
                BuildPriorityLabel(item),
                BuildReasonLabel(item),
                BuildStrategyLabel(item.Summary.RouteKey, item.FailureCount),
                BuildEntryLabel(item.Summary.RouteKey),
                item.ConsecutiveFailureCount > 1,
                item.RiskCount > 0 || item.BrokenCount > 0 || item.Summary.RouteKey is "service" or "broken-uninstall",
                item.Summary.Accent,
                item.Summary.RouteKey))
            .ToList();

        if (recommendations.Count == 0)
        {
            recommendations.Add(new DashboardRecommendation(
                "当前未发现高优先级积压",
                "可以转向历史记录或系统修复模块，执行例行检查与结果复核。",
                "例行检查",
                "当前没有失败趋势或高风险积压",
                "先核对历史后做轻量诊断",
                "建议入口：概览 / 历史记录",
                false,
                false,
                "#0F766E",
                "dashboard"));
        }

        var overviewHeadline = totalIssueCount == 0
            ? "当前未发现明显的待治理积压"
            : failureCount > 0
                ? $"当前共识别 {totalIssueCount} 个待治理候选项，且最近有 {failureCount} 次失败动作需复核"
                : $"当前共识别 {totalIssueCount} 个待治理候选项";

        var overviewDetail = history.Count == 0
            ? "尚无最近操作记录，建议先从概览或软件全景开始扫描。"
            : failureCount > 0
                ? $"最近 {history.Count} 条动作已写入历史，其中失败记录会被优先纳入首页建议。"
                : $"最近 {history.Count} 条动作已写入历史，可回溯核对治理结果。";

        return new DashboardSnapshot(
            summaryItems,
            recommendations,
            history,
            totalIssueCount,
            history.Count,
            overviewHeadline,
            overviewDetail);
    }

    public async Task<IReadOnlyList<ModuleSummary>> BuildSummaryAsync(CancellationToken cancellationToken = default)
    {
        return (await BuildSnapshotAsync(cancellationToken)).SummaryItems;
    }

    private static string BuildRecommendationDetail(DashboardRecommendationInput item)
    {
        var riskFragment = item.RiskCount > 0
            ? $"其中 {item.RiskCount} 项带有高风险或异常健康标记"
            : "当前未命中额外高风险标记";
        var failureFragment = item.FailureCount > 0
            ? $"最近还有 {item.FailureCount} 次失败动作需要复核。"
            : "最近没有同模块失败动作。";

        return item.Summary.RouteKey switch
        {
            "broken-uninstall" => $"当前有 {item.Summary.Count} 个失效卸载条目，{riskFragment}；建议先清理无效入口，减少后续误判。{failureFragment}",
            "startup" => $"当前有 {item.Summary.Count} 个启动项候选，{riskFragment}；优先禁用影响启动速度或来源异常的项。{failureFragment}",
            "context-menu" => $"当前有 {item.Summary.Count} 个右键菜单候选，{riskFragment}；建议先治理残留菜单和失效扩展。{failureFragment}",
            "scheduled-task" => $"当前有 {item.Summary.Count} 个计划任务候选，{riskFragment}；优先检查高频触发和目标缺失的任务。{failureFragment}",
            "service" => $"当前有 {item.Summary.Count} 个系统服务候选，{riskFragment}；建议确认路径与发布者后再处理。{failureFragment}",
            _ => $"当前有 {item.Summary.Count} 个候选项待处理。{failureFragment}"
        };
    }

    private static DashboardRecommendationInput BuildRecommendationInput(ModuleSummary summary, int riskCount, int brokenCount, int failureCount, int consecutiveFailureCount)
    {
        var score = summary.Count + (riskCount * 3) + (brokenCount * 2) + (failureCount * 4) + (consecutiveFailureCount * 5);
        return new DashboardRecommendationInput(summary, riskCount, brokenCount, failureCount, consecutiveFailureCount, score);
    }

    private static string BuildPriorityLabel(DashboardRecommendationInput item)
    {
        return item.ConsecutiveFailureCount > 1
            ? "连续失败"
            : item.FailureCount > 0
            ? "优先复核"
            : item.RiskCount > 0 || item.BrokenCount > 0
                ? "高优先级"
                : "常规优先";
    }

    private static string BuildReasonLabel(DashboardRecommendationInput item)
    {
        if (item.ConsecutiveFailureCount > 1)
        {
            return $"最近连续失败 {item.ConsecutiveFailureCount} 次";
        }

        if (item.FailureCount > 0)
        {
            return $"最近失败 {item.FailureCount} 次";
        }

        if (item.RiskCount > 0)
        {
            return $"高风险或异常项 {item.RiskCount} 个";
        }

        return $"当前候选 {item.Summary.Count} 个";
    }

    private static string BuildStrategyLabel(string routeKey, int failureCount)
    {
        if (failureCount > 1)
        {
            return "暂停重复执行，先定位失败根因";
        }

        if (failureCount > 0)
        {
            return "先复核失败再继续治理";
        }

        return routeKey switch
        {
            "broken-uninstall" => "先核对入口再删除",
            "startup" => "先禁用后观察",
            "context-menu" => "先禁用后清理",
            "scheduled-task" => "先禁用再删除",
            "service" => "先确认路径再卸载",
            _ => "先扫描后执行"
        };
    }

    private static string BuildEntryLabel(string routeKey)
    {
        return routeKey switch
        {
            "broken-uninstall" => "建议入口：失效卸载条目",
            "startup" => "建议入口：启动项",
            "context-menu" => "建议入口：右键菜单",
            "scheduled-task" => "建议入口：计划任务",
            "service" => "建议入口：系统服务",
            _ => "建议入口：概览"
        };
    }

    private static int CountFailures(IEnumerable<OperationLogEntry> history, string module)
    {
        return history.Count(entry =>
            string.Equals(entry.Module, module, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Result, "Failed", StringComparison.OrdinalIgnoreCase));
    }

    private static int CountConsecutiveFailures(IEnumerable<OperationLogEntry> history, string module)
    {
        var ordered = history
            .Where(entry => string.Equals(entry.Module, module, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

        var count = 0;
        foreach (var entry in ordered)
        {
            if (!string.Equals(entry.Result, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private sealed record DashboardRecommendationInput(
        ModuleSummary Summary,
        int RiskCount,
        int BrokenCount,
        int FailureCount,
        int ConsecutiveFailureCount,
        int Score);
}