using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SysCleaner.Application.Services;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Contracts.Models;
using SysCleaner.Domain.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace SysCleaner.Wpf.ViewModels;

public sealed partial class DashboardViewModel(DashboardService dashboardService) : ViewModelBase, IInitializable
{
    public ObservableCollection<ModuleSummary> SummaryItems { get; } = [];
    public ObservableCollection<DashboardRecommendation> Recommendations { get; } = [];
    public ObservableCollection<OperationLogEntry> RecentEntries { get; } = [];

    [ObservableProperty]
    private int _totalIssueCount;

    [ObservableProperty]
    private int _recentActionCount;

    [ObservableProperty]
    private string _overviewHeadline = "等待生成概览";

    [ObservableProperty]
    private string _overviewDetail = "概览会汇总关键治理模块和最近操作。";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在刷新概览", async () =>
        {
            var snapshot = await dashboardService.BuildSnapshotAsync();

            SummaryItems.Clear();
            Recommendations.Clear();
            RecentEntries.Clear();

            foreach (var item in snapshot.SummaryItems)
            {
                SummaryItems.Add(item);
            }

            foreach (var item in snapshot.Recommendations)
            {
                Recommendations.Add(item);
            }

            foreach (var item in snapshot.RecentEntries)
            {
                RecentEntries.Add(item);
            }

            TotalIssueCount = snapshot.TotalIssueCount;
            RecentActionCount = snapshot.RecentActionCount;
            OverviewHeadline = snapshot.OverviewHeadline;
            OverviewDetail = snapshot.OverviewDetail;

            StatusMessage = "已刷新概览。";
        });
    }

    [RelayCommand]
    private void CopyRecentEntry(OperationLogEntry? entry)
    {
        if (entry is null)
        {
            StatusMessage = "当前没有可复制的操作记录。";
            return;
        }

        CopyTextToClipboard(
            string.Join(Environment.NewLine,
            [
                $"时间: {entry.Timestamp:yyyy-MM-dd HH:mm:ss}",
                $"模块: {entry.Module}",
                $"动作: {entry.Action}",
                $"目标: {entry.Target}",
                $"结果: {entry.Result}",
                "详情:",
                entry.Details
            ]),
            "已复制操作记录详情。");
    }

    public Task InitializeAsync() => RefreshAsync();
}

public sealed partial class InstalledAppsViewModel(IInstalledAppService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<InstalledApp> Apps { get; } = [];
    private readonly List<InstalledApp> _allApps = [];

    [ObservableProperty]
    private InstalledApp? _selectedApp;

    [ObservableProperty]
    private bool _showOnlyUninstallable = true;

    [ObservableProperty]
    private bool _showOnlyCurrentUser;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshInternalAsync("正在加载已安装软件列表", SelectedApp?.Id, SelectedApp is null ? null : Apps.IndexOf(SelectedApp));
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task LaunchUninstallAsync()
    {
        if (SelectedApp is null) return;

        if (!ConfirmDangerousAction(
            "启动软件卸载",
            SelectedApp.DisplayName,
            [
                $"发布者：{SelectedApp.Publisher}",
                $"安装目录：{SelectedApp.InstallLocation}"
            ],
            "卸载程序退出后会自动刷新列表。"))
        {
            return;
        }

        var currentAppId = SelectedApp.Id;
        var currentIndex = Apps.IndexOf(SelectedApp);
        var result = await RunBusyAsync($"正在启动 {SelectedApp.DisplayName} 的卸载程序", () => service.LaunchUninstallAsync(SelectedApp));

        await RefreshInternalAsync("正在刷新卸载结果", currentAppId, currentIndex, false);
        StatusMessage = result.Success
            ? $"{result.Message} 当前列表已刷新并保留原位置附近。"
            : result.Message;
    }

    private bool CanAct() => SelectedApp is not null;

    partial void OnSelectedAppChanged(InstalledApp? value) => LaunchUninstallCommand.NotifyCanExecuteChanged();

    partial void OnShowOnlyUninstallableChanged(bool value)
    {
        ApplyFilters(SelectedApp?.Id, SelectedApp is null ? null : Apps.IndexOf(SelectedApp));
        StatusMessage = BuildStatusMessage();
    }

    partial void OnShowOnlyCurrentUserChanged(bool value)
    {
        ApplyFilters(SelectedApp?.Id, SelectedApp is null ? null : Apps.IndexOf(SelectedApp));
        StatusMessage = BuildStatusMessage();
    }

    public Task InitializeAsync() => RefreshAsync();

    private async Task RefreshInternalAsync(string busyMessage, string? anchorAppId, int? anchorIndex, bool updateStatusMessage = true)
    {
        await RunBusyAsync(busyMessage, async () =>
        {
            var apps = await service.GetInstalledAppsAsync();

            _allApps.Clear();
            _allApps.AddRange(apps);
            ApplyFilters(anchorAppId, anchorIndex);

            if (updateStatusMessage)
            {
                StatusMessage = BuildStatusMessage();
            }
        });
    }

    private void ApplyFilters(string? anchorAppId, int? anchorIndex)
    {
        var filteredApps = _allApps
            .Where(app => !ShowOnlyUninstallable || app.IsLikelyUninstallable)
            .Where(app => !ShowOnlyCurrentUser || app.IsCurrentUserInstall)
            .ToList();

        Apps.Clear();
        foreach (var item in filteredApps)
        {
            Apps.Add(item);
        }

        SelectedApp = RestoreSelection(filteredApps, anchorAppId, anchorIndex);
    }

    private string BuildStatusMessage()
    {
        if (_allApps.Count == 0)
        {
            return "当前未发现已安装软件。";
        }

        var filters = new List<string>();
        if (ShowOnlyUninstallable)
        {
            filters.Add("可卸载项");
        }

        if (ShowOnlyCurrentUser)
        {
            filters.Add("当前用户安装项");
        }

        if (filters.Count == 0)
        {
            return $"已加载 {_allApps.Count} 个软件，当前显示全部 {Apps.Count} 项。";
        }

        return $"已加载 {_allApps.Count} 个软件，当前显示 {Apps.Count} 个{string.Join(" + ", filters)}。";
    }

    private static InstalledApp? RestoreSelection(IReadOnlyList<InstalledApp> apps, string? anchorAppId, int? anchorIndex)
    {
        if (apps.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(anchorAppId))
        {
            var matched = apps.FirstOrDefault(app => string.Equals(app.Id, anchorAppId, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                return matched;
            }
        }

        if (anchorIndex.HasValue)
        {
            var index = Math.Max(0, Math.Min(anchorIndex.Value, apps.Count - 1));
            return apps[index];
        }

        return apps[0];
    }
}

public sealed partial class SoftwarePanoramaViewModel(
    IInstalledAppService installedAppService,
    IStartupItemService startupItemService,
    IContextMenuService contextMenuService,
    ITaskSchedulerService taskSchedulerService,
    IServiceControlService serviceControlService,
    ICleanupExecutionService cleanupExecutionService,
    SoftwarePanoramaService panoramaService) : ViewModelBase, IInitializable
{
    public ObservableCollection<InstalledApp> Apps { get; } = [];
    public ObservableCollection<ModuleSummary> SummaryItems { get; } = [];
    public ObservableCollection<CleanupCandidate> Items { get; } = [];

    [ObservableProperty]
    private InstalledApp? _selectedApp;

    [ObservableProperty]
    private CleanupCandidate? _selectedItem;

    [ObservableProperty]
    private string _appNarrative = "请选择一个软件，生成该软件在系统中的治理全景。";

    [RelayCommand]
    private async Task RefreshAppsAsync()
    {
        await RunBusyAsync("正在加载软件全景候选列表", async () =>
        {
            Apps.Clear();
            foreach (var app in await installedAppService.GetInstalledAppsAsync())
            {
                Apps.Add(app);
            }

            SelectedApp ??= Apps.FirstOrDefault();
            StatusMessage = $"已加载 {Apps.Count} 个软件，可生成全景。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (SelectedApp is null)
        {
            return;
        }

        await RunBusyAsync($"正在生成 {SelectedApp.DisplayName} 的软件全景", async () =>
        {
            var snapshot = await panoramaService.BuildAsync(SelectedApp.Id);
            SummaryItems.Clear();
            Items.Clear();

            if (snapshot is null)
            {
                AppNarrative = "未找到对应的软件信息。";
                StatusMessage = "生成软件全景失败。";
                return;
            }

            foreach (var summary in BuildSummary(snapshot))
            {
                SummaryItems.Add(summary);
            }

            foreach (var item in snapshot.AllItems.OrderByDescending(item => item.Health).ThenBy(item => item.Title))
            {
                Items.Add(item);
            }

            AppNarrative = BuildNarrative(snapshot);
            StatusMessage = $"已生成 {snapshot.App.DisplayName} 的治理全景，共识别 {Items.Count} 个候选项。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanLaunchUninstall))]
    private async Task LaunchUninstallAsync()
    {
        if (SelectedApp is null)
        {
            return;
        }

        if (!ConfirmDangerousAction(
                "启动软件卸载",
                SelectedApp.DisplayName,
                [
                    $"发布者：{SelectedApp.Publisher}",
                    $"安装目录：{SelectedApp.InstallLocation}"
                ],
                "卸载程序会按软件原生方式启动。"))
        {
            return;
        }

        var result = await RunBusyAsync($"正在启动 {SelectedApp.DisplayName} 的卸载程序", () => installedAppService.LaunchUninstallAsync(SelectedApp));
        StatusMessage = result.Message;
    }

    [RelayCommand(CanExecute = nameof(CanDisableSelected))]
    private async Task DisableSelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var result = await RunBusyAsync($"正在禁用 {SelectedItem.Title}", () => DispatchDisableAsync(SelectedItem));
        StatusMessage = result.Message;
        await BuildAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var result = await RunBusyAsync($"正在删除 {SelectedItem.Title}", () => DispatchDeleteAsync(SelectedItem));
        StatusMessage = result.Message;
        await BuildAsync();
    }

    private bool CanBuild() => SelectedApp is not null;

    private bool CanLaunchUninstall() => SelectedApp is not null;

    private bool CanDisableSelected()
    {
        return SelectedItem is not null
            && SelectedItem.CanDisable
            && SelectedItem.Category is SysCleaner.Domain.Enums.CleanupCategory.StartupEntry
                or SysCleaner.Domain.Enums.CleanupCategory.ContextMenuEntry
                or SysCleaner.Domain.Enums.CleanupCategory.ScheduledTask;
    }

    private bool CanDeleteSelected() => SelectedItem is not null && SelectedItem.CanDelete;

    partial void OnSelectedAppChanged(InstalledApp? value)
    {
        BuildCommand.NotifyCanExecuteChanged();
        LaunchUninstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedItemChanged(CleanupCandidate? value)
    {
        DisableSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync() => await RefreshAppsAsync();

    private Task<OperationResult> DispatchDisableAsync(CleanupCandidate candidate)
    {
        return candidate.Category switch
        {
            SysCleaner.Domain.Enums.CleanupCategory.StartupEntry => startupItemService.DisableAsync(candidate),
            SysCleaner.Domain.Enums.CleanupCategory.ContextMenuEntry => contextMenuService.DisableAsync(candidate),
            SysCleaner.Domain.Enums.CleanupCategory.ScheduledTask => taskSchedulerService.DisableAsync(candidate),
            _ => Task.FromResult(new OperationResult(false, "当前候选项不支持禁用。"))
        };
    }

    private Task<OperationResult> DispatchDeleteAsync(CleanupCandidate candidate)
    {
        return candidate.Category switch
        {
            SysCleaner.Domain.Enums.CleanupCategory.StartupEntry => startupItemService.DeleteAsync(candidate),
            SysCleaner.Domain.Enums.CleanupCategory.ContextMenuEntry => contextMenuService.DeleteAsync(candidate),
            SysCleaner.Domain.Enums.CleanupCategory.ScheduledTask => taskSchedulerService.DeleteAsync(candidate),
            SysCleaner.Domain.Enums.CleanupCategory.Service => serviceControlService.DeleteAsync(candidate),
            _ => cleanupExecutionService.DeleteAsync(candidate)
        };
    }

    private static string BuildNarrative(SoftwarePanoramaSnapshot snapshot)
    {
        var installPath = string.IsNullOrWhiteSpace(snapshot.App.InstallLocation) ? "未记录安装目录" : snapshot.App.InstallLocation;
        return $"{snapshot.App.DisplayName} | 发布者：{snapshot.App.Publisher} | 版本：{snapshot.App.Version} | 健康度：{snapshot.App.Health} | 安装位置：{installPath}";
    }

    private static IReadOnlyList<ModuleSummary> BuildSummary(SoftwarePanoramaSnapshot snapshot)
    {
        return
        [
            new ModuleSummary("残留项", "文件与目录残留", snapshot.Residues.Count, "#C2410C", "residue"),
            new ModuleSummary("注册表", "与该软件相关的注册表候选项", snapshot.RegistryEntries.Count, "#1D4ED8", "registry"),
            new ModuleSummary("启动项", "命中该软件的开机启动项", snapshot.StartupItems.Count, "#0F766E", "startup"),
            new ModuleSummary("右键菜单", "命中该软件的右键菜单项", snapshot.ContextMenuItems.Count, "#7C3AED", "context-menu"),
            new ModuleSummary("计划任务", "命中该软件的计划任务", snapshot.ScheduledTasks.Count, "#0F172A", "scheduled-task"),
            new ModuleSummary("系统服务", "命中该软件的服务项", snapshot.Services.Count, "#B45309", "service")
        ];
    }
}

public sealed partial class BrokenEntriesViewModel(IInstalledAppService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<InstalledApp> Items { get; } = [];

    [ObservableProperty]
    private InstalledApp? _selectedItem;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在扫描失效卸载条目", async () =>
        {
            Items.Clear();
            foreach (var item in await service.GetBrokenUninstallEntriesAsync())
            {
                Items.Add(item);
            }

            StatusMessage = $"已找到 {Items.Count} 个失效卸载条目。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveSelectedAsync()
    {
        if (SelectedItem is null) return;

        if (!ConfirmDangerousAction(
            "删除失效卸载条目",
            SelectedItem.DisplayName,
            [
                $"发布者：{SelectedItem.Publisher}",
                $"注册表位置：{SelectedItem.RegistryPath}"
            ],
            "该操作会移除控制面板中的残留卸载入口。"))
        {
            return;
        }

        var result = await RunBusyAsync($"正在移除失效卸载条目：{SelectedItem.DisplayName}", () => service.RemoveBrokenEntryAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    private bool CanRemove() => SelectedItem is not null && !SelectedItem.IsProtected;

    partial void OnSelectedItemChanged(InstalledApp? value) => RemoveSelectedCommand.NotifyCanExecuteChanged();

    public Task InitializeAsync() => RefreshAsync();
}

public sealed partial class ScheduledTasksViewModel(ITaskSchedulerService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<CleanupCandidate> Items { get; } = [];

    [ObservableProperty]
    private CleanupCandidate? _selectedItem;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在刷新计划任务列表", async () =>
        {
            Items.Clear();
            foreach (var item in await service.GetTasksAsync())
            {
                Items.Add(item);
            }

            StatusMessage = $"已加载 {Items.Count} 个计划任务候选项。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DisableAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var result = await RunBusyAsync($"正在禁用计划任务：{SelectedItem.Title}", () => service.DisableAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DeleteAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var result = await RunBusyAsync($"正在删除计划任务：{SelectedItem.Title}", () => service.DeleteAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    private bool CanAct() => SelectedItem is not null;

    partial void OnSelectedItemChanged(CleanupCandidate? value)
    {
        DisableCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    public Task InitializeAsync() => RefreshAsync();
}

public sealed partial class SystemServicesViewModel(IServiceControlService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<CleanupCandidate> Items { get; } = [];

    [ObservableProperty]
    private CleanupCandidate? _selectedItem;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在刷新系统服务列表", async () =>
        {
            Items.Clear();
            foreach (var item in await service.GetServicesAsync())
            {
                Items.Add(item);
            }

            StatusMessage = $"已加载 {Items.Count} 个系统服务候选项。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (!ConfirmDangerousAction(
            "卸载系统服务",
            SelectedItem.Title,
            [
                $"服务信息：{SelectedItem.Source}",
                $"目标文件：{SelectedItem.TargetPath}"
            ],
            "该操作会尝试停止服务并删除服务注册。"))
        {
            return;
        }

        var result = await RunBusyAsync($"正在删除系统服务：{SelectedItem.Title}", () => service.DeleteAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    private bool CanDelete() => SelectedItem is not null && SelectedItem.CanDelete;

    partial void OnSelectedItemChanged(CleanupCandidate? value) => DeleteCommand.NotifyCanExecuteChanged();

    public Task InitializeAsync() => RefreshAsync();
}

public sealed partial class StartupViewModel(IStartupItemService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<CleanupCandidate> Items { get; } = [];

    [ObservableProperty]
    private CleanupCandidate? _selectedItem;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在刷新启动项列表", async () =>
        {
            Items.Clear();
            foreach (var item in await service.GetStartupItemsAsync())
            {
                Items.Add(item);
            }

            StatusMessage = $"已加载 {Items.Count} 个启动项。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DisableAsync()
    {
        if (SelectedItem is null) return;
        var result = await RunBusyAsync($"正在禁用启动项：{SelectedItem.Title}", () => service.DisableAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DeleteAsync()
    {
        if (SelectedItem is null) return;
        var result = await RunBusyAsync($"正在删除启动项：{SelectedItem.Title}", () => service.DeleteAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    private bool CanAct() => SelectedItem is not null;

    partial void OnSelectedItemChanged(CleanupCandidate? value)
    {
        DisableCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    public Task InitializeAsync() => RefreshAsync();
}

public sealed partial class ContextMenuViewModel(IContextMenuService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<CleanupCandidate> Items { get; } = [];

    [ObservableProperty]
    private CleanupCandidate? _selectedItem;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在刷新右键菜单项", async () =>
        {
            Items.Clear();
            foreach (var item in await service.GetEntriesAsync())
            {
                Items.Add(item);
            }

            StatusMessage = $"已加载 {Items.Count} 个右键菜单项。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DisableAsync()
    {
        if (SelectedItem is null) return;
        var result = await RunBusyAsync($"正在禁用右键菜单项：{SelectedItem.Title}", () => service.DisableAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DeleteAsync()
    {
        if (SelectedItem is null) return;
        var result = await RunBusyAsync($"正在删除右键菜单项：{SelectedItem.Title}", () => service.DeleteAsync(SelectedItem));
        StatusMessage = result.Message;
        await RefreshAsync();
    }

    private bool CanAct() => SelectedItem is not null;

    partial void OnSelectedItemChanged(CleanupCandidate? value)
    {
        DisableCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    public Task InitializeAsync() => RefreshAsync();
}

public sealed partial class UnlockAssistantViewModel(ILockDetectionService detectionService, IUnlockAssistanceService unlockService) : ViewModelBase, IInitializable
{
    public ObservableCollection<LockInfo> LockItems { get; } = [];

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    private LockInfo? _selectedItem;

    [RelayCommand]
    private void BrowseTargetFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要检测占用的文件",
            CheckFileExists = false,
            FileName = File.Exists(TargetPath) ? Path.GetFileName(TargetPath) : string.Empty,
            InitialDirectory = ResolveInitialDirectory(TargetPath)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        TargetPath = dialog.FileName;
        StatusMessage = $"已选择目标文件：{TargetPath}";
    }

    [RelayCommand]
    private void BrowseTargetFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要检测占用的文件夹",
            InitialDirectory = ResolveInitialDirectory(TargetPath)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        TargetPath = dialog.FolderName;
        StatusMessage = $"已选择目标文件夹：{TargetPath}";
    }

    [RelayCommand]
    private async Task AcceptDroppedTargetPathsAsync(string[]? paths)
    {
        if (paths is null || paths.Length == 0)
        {
            StatusMessage = "未识别到可用的拖拽目标。";
            return;
        }

        if (paths.Length > 1)
        {
            ShowNotice(
                "多目标拖拽提示",
                $"本次拖入了 {paths.Length} 个项目。系统会逐个检测并汇总结果，目标框保留第一个路径。",
                MessageBoxImage.Information);
        }

        var normalizedPaths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var path = normalizedPaths[0];

        TargetPath = path;
        StatusMessage = File.Exists(path)
            ? $"已通过拖拽选择目标文件：{TargetPath}"
            : $"已通过拖拽选择目标文件夹：{TargetPath}";

        await DetectTargetsAsync(normalizedPaths);
    }

    [RelayCommand]
    private async Task DetectAsync()
    {
        var resolvedTargetPath = ResolveExistingTargetPath();
        if (resolvedTargetPath is null)
        {
            return;
        }

        await DetectTargetsAsync([resolvedTargetPath]);
    }

    [RelayCommand(CanExecute = nameof(CanCloseProcess))]
    private async Task CloseProcessAsync()
    {
        if (SelectedItem is null) return;

        await CloseProcessItemAsync(SelectedItem);
    }

    [RelayCommand]
    private async Task CloseFirstTerminableInGroupAsync(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            StatusMessage = "当前分组没有可处理的目标路径。";
            return;
        }

        var candidate = LockItems.FirstOrDefault(item => string.Equals(item.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase) && item.CanTerminate);
        if (candidate is null)
        {
            var sample = LockItems.FirstOrDefault(item => string.Equals(item.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));
            if (sample is not null)
            {
                var blocked = BuildCloseProcessBlockedMessage(sample);
                ShowNotice(blocked.Title, blocked.Message, blocked.Icon);
                StatusMessage = $"分组“{targetPath}”中没有可直接关闭的占用进程。";
            }
            else
            {
                StatusMessage = "当前分组没有可直接关闭的占用进程。";
            }

            return;
        }

        SelectedItem = candidate;
        await CloseProcessItemAsync(candidate);
    }

    [RelayCommand]
    private async Task RunRecommendedActionAsync(LockInfo? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;

        if (item.CanTerminate)
        {
            await CloseProcessItemAsync(item);
            return;
        }

        if (item.Kind == SysCleaner.Domain.Enums.LockKind.Shell)
        {
            await RestartExplorerAsync();
            return;
        }

        var blocked = BuildCloseProcessBlockedMessage(item);
        ShowNotice(blocked.Title, blocked.Message, blocked.Icon);
        StatusMessage = blocked.Message;
    }

    private async Task CloseProcessItemAsync(LockInfo item)
    {
        if (!item.CanTerminate)
        {
            var blocked = BuildCloseProcessBlockedMessage(item);
            ShowNotice(blocked.Title, blocked.Message, blocked.Icon);
            StatusMessage = blocked.Message;
            return;
        }

        if (!ConfirmDangerousAction(
            "关闭占用进程",
            item.HolderName,
            [
                $"PID：{item.ProcessId}",
                $"路径：{item.HolderPath}"
            ],
            "关闭错误进程可能导致数据丢失，请先确认目标。"))
        {
            return;
        }

        var result = await RunBusyAsync($"正在关闭占用进程：{item.HolderName}", () => unlockService.CloseProcessAsync(item));
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        var result = await RunBusyAsync("正在重启资源管理器", () => unlockService.RestartExplorerAsync());
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private async Task ForceDeleteTargetAsync(string? targetPath)
    {
        var resolvedTargetPath = ResolveExistingTargetPath(targetPath);
        if (resolvedTargetPath is null)
        {
            return;
        }

        if (!ConfirmDangerousAction(
            "强制删除目标",
            ResolveTargetDisplayName(resolvedTargetPath),
            [
                $"路径：{resolvedTargetPath}",
                $"类型：{(Directory.Exists(resolvedTargetPath) ? "文件夹" : "文件")}",
                "执行内容：接管所有权、授予管理员权限并尝试立即删除"
            ],
            "该操作会修改目标权限和属性，删除后不可恢复；如果目标仍被占用，请改用“重启后删除”。"))
        {
            return;
        }

        TargetPath = resolvedTargetPath;
        var result = await RunBusyAsync("正在强制删除目标", () => unlockService.ForceDeleteAsync(resolvedTargetPath));
        if (result.Success)
        {
            RemoveLockItemsForTarget(resolvedTargetPath);
        }

        StatusMessage = result.Message;
    }

    [RelayCommand]
    private async Task ScheduleDeleteAsync()
    {
        var resolvedTargetPath = ResolveExistingTargetPath();
        if (resolvedTargetPath is null)
        {
            return;
        }

        var result = await RunBusyAsync("正在安排重启后删除目标", () => unlockService.ScheduleDeleteOnRebootAsync(resolvedTargetPath));
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private void CopyGroupTargetPath(string? targetPath)
    {
        CopyTextToClipboard(targetPath, "已复制分组目标路径。", "当前分组没有可复制的目标路径。");
    }

    [RelayCommand]
    private async Task DetectGroupTargetAsync(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            StatusMessage = "当前分组没有可重新识别的目标路径。";
            return;
        }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            StatusMessage = "该分组目标路径已不存在，无法重新识别。";
            return;
        }

        TargetPath = targetPath;
        await DetectTargetsAsync([targetPath]);
    }

    private bool CanCloseProcess() => SelectedItem is not null;

    private static (string Title, string Message, MessageBoxImage Icon) BuildCloseProcessBlockedMessage(LockInfo item)
    {
        if (item.Kind == SysCleaner.Domain.Enums.LockKind.Protected || item.Risk == SysCleaner.Domain.Enums.RiskLevel.Protected)
        {
            return (
                "无法关闭受保护进程",
                $"当前占用方“{item.HolderName}”属于受保护或高风险系统进程，不能直接结束。\n\n建议先关闭相关应用，或改用重启资源管理器等更安全的操作。",
                MessageBoxImage.Warning);
        }

        if (item.Kind == SysCleaner.Domain.Enums.LockKind.Service)
        {
            return (
                "当前占用来自系统服务",
                $"当前占用方“{item.HolderName}”被识别为服务，不支持按普通进程直接关闭。\n\n建议先停止对应服务，或在系统服务模块中处理。",
                MessageBoxImage.Information);
        }

        if (item.Kind == SysCleaner.Domain.Enums.LockKind.Shell)
        {
            return (
                "当前占用来自资源管理器",
                $"当前占用方“{item.HolderName}”属于资源管理器或壳层相关进程，建议优先使用“重启资源管理器”，不要直接强制结束。",
                MessageBoxImage.Information);
        }

        return (
            "当前项目不支持直接关闭",
            $"当前占用方“{item.HolderName}”不支持直接结束进程。\n\n建议根据结果中的“建议”列选择更合适的处理方式。",
            MessageBoxImage.Information);
    }

    private async Task DetectTargetsAsync(IReadOnlyList<string> targetPaths)
    {
        await RunBusyAsync(targetPaths.Count > 1 ? "正在汇总多个目标的占用情况" : "正在检测文件占用情况", async () =>
        {
            LockItems.Clear();

            var detectedTargetCount = 0;
            for (var index = 0; index < targetPaths.Count; index++)
            {
                var targetPath = targetPaths[index];
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    continue;
                }

                ReportBusyProgress($"正在检测占用 ({index + 1}/{targetPaths.Count})", index + 1, targetPaths.Count);

                var detected = await detectionService.DetectLocksAsync(targetPath);
                if (detected.Count > 0)
                {
                    detectedTargetCount++;
                }

                foreach (var item in detected)
                {
                    LockItems.Add(item with { TargetPath = targetPath });
                }
            }

            SelectedItem = LockItems.FirstOrDefault();

            if (targetPaths.Count == 1)
            {
                StatusMessage = LockItems.Count == 0 ? "未检测到明显占用。" : $"已识别 {LockItems.Count} 个占用方。";
                return;
            }

            StatusMessage = LockItems.Count == 0
                ? $"已完成 {targetPaths.Count} 个目标的占用识别，未检测到明显占用。"
                : $"已完成 {targetPaths.Count} 个目标的占用识别，其中 {detectedTargetCount} 个目标检测到占用，共识别 {LockItems.Count} 个占用方。";
        });
    }

    private static string ResolveInitialDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (Directory.Exists(path))
            {
                return path;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private string? ResolveExistingTargetPath(string? candidatePath = null)
    {
        var targetPath = string.IsNullOrWhiteSpace(candidatePath) ? TargetPath : candidatePath;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            StatusMessage = "请先选择目标文件或文件夹。";
            return null;
        }

        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            StatusMessage = "目标路径不存在，请重新选择。";
            return null;
        }

        return targetPath;
    }

    private void RemoveLockItemsForTarget(string targetPath)
    {
        for (var index = LockItems.Count - 1; index >= 0; index--)
        {
            if (string.Equals(LockItems[index].TargetPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                LockItems.RemoveAt(index);
            }
        }

        SelectedItem = LockItems.FirstOrDefault();
    }

    private static string ResolveTargetDisplayName(string targetPath)
    {
        var normalizedPath = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(normalizedPath)
            ? targetPath
            : Path.GetFileName(normalizedPath);
    }

    partial void OnSelectedItemChanged(LockInfo? value) => CloseProcessCommand.NotifyCanExecuteChanged();

    public Task InitializeAsync() => Task.CompletedTask;
}

public sealed partial class EmptyCleanupItemViewModel : ObservableObject
{
    public EmptyCleanupItemViewModel(CleanupCandidate candidate)
    {
        Candidate = candidate;
    }

    public CleanupCandidate Candidate { get; }

    [ObservableProperty]
    private bool _isSelected = true;
}

public sealed partial class EmptyCleanupViewModel(IEmptyItemScanService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<EmptyCleanupItemViewModel> Items { get; } = [];
    public ObservableCollection<CleanupCandidate> CascadeDeleted { get; } = [];

    [ObservableProperty]
    private EmptyCleanupItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    [RelayCommand]
    private void BrowseRootPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要扫描的文件夹",
            InitialDirectory = Directory.Exists(RootPath)
                ? RootPath
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        RootPath = dialog.FolderName;
        StatusMessage = $"已选择扫描目录：{RootPath}";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (!ValidateRootPath())
        {
            return;
        }

        await RunBusyAsync($"正在扫描空项：{RootPath}", async () =>
        {
            Items.Clear();
            CascadeDeleted.Clear();
            foreach (var item in await service.ScanAsync(RootPath))
            {
                Items.Add(new EmptyCleanupItemViewModel(item));
            }

            StatusMessage = $"已扫描 {Items.Count} 个空项。";
        });
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (!ValidateRootPath())
        {
            return;
        }

        await RunBusyAsync("正在执行空项清理", async () =>
        {
            CascadeDeleted.Clear();
            var selected = Items.Where(x => x.IsSelected).Select(x => x.Candidate).ToList();
            var cascaded = await service.ExecuteAsync(RootPath, selected);
            foreach (var item in cascaded)
            {
                CascadeDeleted.Add(item);
            }

            StatusMessage = $"已执行 {selected.Count} 个空项删除，级联删除 {CascadeDeleted.Count} 个父目录。";
            await ScanAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelectedItem))]
    private void ToggleSelectedItem()
    {
        if (SelectedItem is null)
        {
            return;
        }

        SelectedItem.IsSelected = !SelectedItem.IsSelected;
        StatusMessage = SelectedItem.IsSelected
            ? $"已勾选 {SelectedItem.Candidate.Title}。"
            : $"已取消勾选 {SelectedItem.Candidate.Title}。";
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelectedItem))]
    private void SelectOnlyCurrentItem()
    {
        if (SelectedItem is null)
        {
            return;
        }

        foreach (var item in Items)
        {
            item.IsSelected = item == SelectedItem;
        }

        StatusMessage = $"已仅保留 {SelectedItem.Candidate.Title} 为选中项。";
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelectedItem))]
    private void CopySelectedPath()
    {
        if (SelectedItem is null)
        {
            return;
        }

        CopyTextToClipboard(SelectedItem.Candidate.TargetPath, "已复制空项路径。");
    }

    private bool CanActOnSelectedItem() => SelectedItem is not null;

    partial void OnSelectedItemChanged(EmptyCleanupItemViewModel? value)
    {
        ToggleSelectedItemCommand.NotifyCanExecuteChanged();
        SelectOnlyCurrentItemCommand.NotifyCanExecuteChanged();
        CopySelectedPathCommand.NotifyCanExecuteChanged();
    }

    private bool ValidateRootPath()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            StatusMessage = "请先选择要扫描的文件夹。";
            return false;
        }

        if (!Directory.Exists(RootPath))
        {
            StatusMessage = "所选文件夹不存在，请重新选择。";
            return false;
        }

        return true;
    }

    public Task InitializeAsync() => Task.CompletedTask;
}

public sealed partial class ResidueViewModel(IInstalledAppService appService, IResidueAnalysisService residueService, ICleanupExecutionService cleanupExecutionService) : ViewModelBase, IInitializable
{
    public ObservableCollection<InstalledApp> Apps { get; } = [];
    public ObservableCollection<CleanupCandidate> Items { get; } = [];

    [ObservableProperty]
    private InstalledApp? _selectedApp;

    [ObservableProperty]
    private CleanupCandidate? _selectedItem;

    [RelayCommand]
    private async Task RefreshAppsAsync()
    {
        await RunBusyAsync("正在加载残留扫描软件列表", async () =>
        {
            Apps.Clear();
            foreach (var app in await appService.GetInstalledAppsAsync())
            {
                Apps.Add(app);
            }

            SelectedApp ??= Apps.FirstOrDefault();
            StatusMessage = $"已加载 {Apps.Count} 个软件，可执行残留扫描。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (SelectedApp is null) return;
        await RunBusyAsync($"正在扫描 {SelectedApp.DisplayName} 的残留项", async () =>
        {
            Items.Clear();
            foreach (var item in await residueService.ScanAsync(SelectedApp))
            {
                Items.Add(item);
            }

            StatusMessage = $"已找到 {Items.Count} 个残留候选项。";
        });
    }

    private bool CanScan() => SelectedApp is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedItem is null) return;

        if (!ConfirmDangerousAction(
            "删除残留项",
            SelectedItem.Title,
            [
                $"路径：{SelectedItem.TargetPath}",
                $"证据：{SelectedItem.Evidence}"
            ],
            "建议确认该路径确实只属于目标软件后再删除。"))
        {
            return;
        }

        var result = await RunBusyAsync($"正在删除残留项：{SelectedItem.Title}", () => cleanupExecutionService.DeleteAsync(SelectedItem));
        StatusMessage = result.Message;
        await ScanAsync();
    }

    private bool CanDeleteSelected() => SelectedItem is not null;

    partial void OnSelectedAppChanged(InstalledApp? value) => ScanCommand.NotifyCanExecuteChanged();
    partial void OnSelectedItemChanged(CleanupCandidate? value) => DeleteSelectedCommand.NotifyCanExecuteChanged();

    public async Task InitializeAsync() => await RefreshAppsAsync();
}

public sealed partial class RegistryViewModel(IInstalledAppService appService, IRegistryCleanupService registryService, ICleanupExecutionService cleanupExecutionService) : ViewModelBase, IInitializable
{
    public ObservableCollection<InstalledApp> Apps { get; } = [];
    public ObservableCollection<CleanupCandidate> Items { get; } = [];
    public ObservableCollection<string> AvailableSources { get; } = [];
    private readonly List<CleanupCandidate> _allItems = [];
    private bool _lastScanWasGlobal;

    [ObservableProperty]
    private InstalledApp? _selectedApp;

    [ObservableProperty]
    private CleanupCandidate? _selectedItem;

    [ObservableProperty]
    private bool _showOnlyHighConfidence;

    [ObservableProperty]
    private string? _selectedSource;

    [RelayCommand]
    private async Task RefreshAppsAsync()
    {
        await RunBusyAsync("正在加载注册表扫描软件列表", async () =>
        {
            Apps.Clear();
            foreach (var app in await appService.GetInstalledAppsAsync())
            {
                Apps.Add(app);
            }

            SelectedApp ??= Apps.FirstOrDefault();
            StatusMessage = $"已加载 {Apps.Count} 个软件，可执行注册表扫描。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (SelectedApp is null) return;
        await RunBusyAsync($"正在扫描 {SelectedApp.DisplayName} 的注册表残留", async () =>
        {
            _lastScanWasGlobal = false;
            _allItems.Clear();
            _allItems.AddRange(await registryService.ScanAsync(SelectedApp));
            ApplyFilters();

            StatusMessage = $"已找到 {Items.Count} 个与 {SelectedApp.DisplayName} 关联的注册表候选项。";
        });
    }

    [RelayCommand]
    private async Task ScanBrokenEntriesAsync()
    {
        await RunBusyAsync("正在执行全局失效注册表扫描", async () =>
        {
            _lastScanWasGlobal = true;
            _allItems.Clear();
            _allItems.AddRange(await registryService.ScanBrokenEntriesAsync());
            ApplyFilters();

            StatusMessage = $"已找到 {Items.Count} 个全局失效注册表项。";
        });
    }

    private bool CanScan() => SelectedApp is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedItem is null) return;
        var result = await RunBusyAsync($"正在删除注册表候选项：{SelectedItem.Title}", () => cleanupExecutionService.DeleteAsync(SelectedItem));
        StatusMessage = result.Message;
        if (_lastScanWasGlobal)
        {
            await ScanBrokenEntriesAsync();
        }
        else
        {
            await ScanAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedSource))]
    private async Task DeleteSelectedSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSource))
        {
            return;
        }

        var candidates = Items.Where(item => item.Source.Equals(SelectedSource, StringComparison.OrdinalIgnoreCase) && item.CanDelete).ToList();
        if (candidates.Count == 0)
        {
            StatusMessage = "当前分组没有可清理项。";
            return;
        }

        var confirmation = BuildSourceCleanupConfirmation(SelectedSource, candidates.Count);
        if (!ConfirmAction(confirmation.Title, confirmation.Message, confirmation.Icon))
        {
            return;
        }

        await RunBusyAsync($"正在清理分组“{SelectedSource}”", async () =>
        {
            var deleted = 0;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                ReportBusyProgress($"正在清理分组“{SelectedSource}” ({index + 1}/{candidates.Count})", index + 1, candidates.Count);
                var result = await cleanupExecutionService.DeleteAsync(candidate);
                if (result.Success)
                {
                    deleted++;
                }
            }

            StatusMessage = $"已清理分组“{SelectedSource}”中的 {deleted}/{candidates.Count} 个项目。";
            if (_lastScanWasGlobal)
            {
                await ScanBrokenEntriesAsync();
            }
            else
            {
                await ScanAsync();
            }
        });
    }

    private bool CanDeleteSelected() => SelectedItem is not null;

    private bool CanDeleteSelectedSource() => !string.IsNullOrWhiteSpace(SelectedSource);

    partial void OnShowOnlyHighConfidenceChanged(bool value) => ApplyFilters();

    partial void OnSelectedSourceChanged(string? value) => DeleteSelectedSourceCommand.NotifyCanExecuteChanged();

    partial void OnSelectedAppChanged(InstalledApp? value) => ScanCommand.NotifyCanExecuteChanged();
    partial void OnSelectedItemChanged(CleanupCandidate? value) => DeleteSelectedCommand.NotifyCanExecuteChanged();

    private void ApplyFilters()
    {
        Items.Clear();
        IEnumerable<CleanupCandidate> candidates = _allItems;
        if (ShowOnlyHighConfidence)
        {
            candidates = candidates.Where(IsHighConfidenceBrokenItem);
        }

        foreach (var item in candidates)
        {
            Items.Add(item);
        }

        AvailableSources.Clear();
        foreach (var source in Items.Select(item => item.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(source => source))
        {
            AvailableSources.Add(source);
        }

        if (!string.IsNullOrWhiteSpace(SelectedSource) && !AvailableSources.Contains(SelectedSource))
        {
            SelectedSource = null;
        }
        else if (string.IsNullOrWhiteSpace(SelectedSource))
        {
            SelectedSource = AvailableSources.FirstOrDefault();
        }

        DeleteSelectedSourceCommand.NotifyCanExecuteChanged();
    }

    private static bool IsHighConfidenceBrokenItem(CleanupCandidate candidate)
    {
        return candidate.Health == SysCleaner.Domain.Enums.ItemHealth.Broken
            && candidate.Risk == SysCleaner.Domain.Enums.RiskLevel.Safe
            && candidate.Evidence.Contains("不存在", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Title, string Message, MessageBoxImage Icon) BuildSourceCleanupConfirmation(string source, int count)
    {
        if (source.Contains("MuiCache", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "清理 MuiCache 缓存",
                $"确认清理分组“{source}”下的 {count} 个项目吗？\n\n这类项目通常只是程序显示名称和路径缓存，目标文件不存在时一般可安全清理。",
                MessageBoxImage.Question);
        }

        if (source.Contains("失效卸载条目", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "清理失效卸载条目",
                $"确认清理分组“{source}”下的 {count} 个项目吗？\n\n这类项目通常表示卸载命令和安装目录都已失效。清理后不会卸载软件本体，只会移除残留注册信息。",
                MessageBoxImage.Warning);
        }

        if (source.Contains("SharedDlls", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "清理 SharedDlls 引用",
                $"确认清理分组“{source}”下的 {count} 个项目吗？\n\nSharedDlls 属于系统级共享库引用计数。虽然当前仅展示目标缺失项，但仍建议再次确认这些路径不是系统组件或被其他软件动态恢复的项。",
                MessageBoxImage.Warning);
        }

        if (source.Contains("协议处理器", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "清理协议处理器",
                $"确认清理分组“{source}”下的 {count} 个项目吗？\n\n协议处理器会影响类似 custom:// 的链接唤起。清理后，对应协议可能无法再被系统或浏览器打开。",
                MessageBoxImage.Warning);
        }

        if (source.Contains("文件关联命令", StringComparison.OrdinalIgnoreCase)
            || source.Contains("右键命令", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "清理命令关联项",
                $"确认清理分组“{source}”下的 {count} 个项目吗？\n\n这些项目会影响文件打开、打印、编辑或右键菜单动作。当前仅展示目标程序缺失的项，通常可清理。",
                MessageBoxImage.Warning);
        }

        if (source.Contains("Shell 扩展", StringComparison.OrdinalIgnoreCase)
            || source.Contains("BHO", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "清理扩展处理器",
                $"确认清理分组“{source}”下的 {count} 个项目吗？\n\n这类项目涉及 Explorer 或浏览器扩展加载链。当前仅展示组件文件已缺失的项，但若来源复杂，仍建议先核对一次。",
                MessageBoxImage.Warning);
        }

        return (
            "批量清理注册表分组",
            $"确认批量清理分组“{source}”下的 {count} 个项目吗？\n\n建议确认这些项目确实属于失效或残留项后再执行。",
            MessageBoxImage.Warning);
    }

    public async Task InitializeAsync() => await RefreshAppsAsync();
}

public sealed partial class HistoryViewModel(IHistoryService historyService) : ViewModelBase, IInitializable
{
    public ObservableCollection<OperationLogEntry> Items { get; } = [];

    [ObservableProperty]
    private OperationLogEntry? _selectedItem;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在加载历史记录", async () =>
        {
            Items.Clear();
            foreach (var item in await historyService.GetRecentAsync())
            {
                Items.Add(item);
            }

            StatusMessage = $"已加载 {Items.Count} 条历史记录。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCopySelected))]
    private void CopySelectedEntry()
    {
        if (SelectedItem is null)
        {
            return;
        }

        CopyTextToClipboard(
            string.Join(Environment.NewLine,
            [
                $"时间: {SelectedItem.Timestamp:yyyy-MM-dd HH:mm:ss}",
                $"模块: {SelectedItem.Module}",
                $"动作: {SelectedItem.Action}",
                $"目标: {SelectedItem.Target}",
                $"结果: {SelectedItem.Result}",
                "详情:",
                SelectedItem.Details
            ]),
            "已复制历史记录详情。");
    }

    private bool CanCopySelected() => SelectedItem is not null;

    partial void OnSelectedItemChanged(OperationLogEntry? value) => CopySelectedEntryCommand.NotifyCanExecuteChanged();

    public Task InitializeAsync() => RefreshAsync();
}