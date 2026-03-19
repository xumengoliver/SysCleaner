using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;

namespace SysCleaner.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly HashSet<object> _initializedViewModels = [];

    public ShellViewModel(
        DashboardViewModel dashboard,
        SoftwarePanoramaViewModel softwarePanorama,
        InstalledAppsViewModel installedApps,
        BrokenEntriesViewModel brokenEntries,
        ContextMenuViewModel contextMenu,
        StartupViewModel startup,
        ScheduledTasksViewModel scheduledTasks,
        SystemServicesViewModel systemServices,
        SystemRepairViewModel systemRepair,
        WindowsUpdateRepairViewModel windowsUpdateRepair,
        UnlockAssistantViewModel unlockAssistant,
        ResidueViewModel residue,
        RegistryViewModel registry,
        RegistrySearchViewModel registrySearch,
        EmptyCleanupViewModel emptyCleanup,
        HistoryViewModel history)
    {
        NavigationItems =
        [
            new NavigationItem("总览", "概览", "整体健康与最近治理状态", dashboard),
            new NavigationItem("软件治理", "软件全景", "按软件聚合残留、启动项、右键菜单与计划任务", softwarePanorama),
            new NavigationItem("软件治理", "软件卸载", "查看安装软件并启动卸载", installedApps),
            new NavigationItem("软件治理", "失效卸载条目", "清理卸载列表中的无效项", brokenEntries),
            new NavigationItem("入口治理", "右键菜单", "治理命令型菜单与 Shell 扩展", contextMenu),
            new NavigationItem("入口治理", "启动项", "管理开机启动项与无效项", startup),
            new NavigationItem("入口治理", "计划任务", "治理第三方计划任务与失效任务", scheduledTasks),
            new NavigationItem("系统治理", "系统服务", "扫描第三方服务并卸载残留服务项", systemServices),
            new NavigationItem("系统治理", "系统修复", "修复图标缓存异常与 Windows 账号头像不同步", systemRepair),
            new NavigationItem("系统治理", "系统更新", "检测 Windows Update 服务、缓存和组件修复状态", windowsUpdateRepair),
            new NavigationItem("系统治理", "解除占用", "定位谁在占用文件并辅助释放", unlockAssistant),
            new NavigationItem("残留治理", "残留清理", "扫描软件残留文件与目录", residue),
            new NavigationItem("残留治理", "注册表清理", "扫描与软件关联的注册表残留", registry),
            new NavigationItem("残留治理", "注册表搜索", "搜索注册表并批量删除或批量编辑搜索结果", registrySearch),
            new NavigationItem("残留治理", "空项清理", "扫描空文件、空目录并级联清理", emptyCleanup),
            new NavigationItem("审计记录", "历史记录", "查看已执行动作与结果", history)
        ];

        SelectedItem = NavigationItems[0];
    }

    public IReadOnlyList<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    public object? CurrentViewModel => SelectedItem?.ViewModel;

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item == value;
        }

        OnPropertyChanged(nameof(CurrentViewModel));
        if (value is not null)
        {
            _ = InitializeItemAsync(value);
        }
    }

    [RelayCommand]
    private void NavigateToRoute(string? routeKey)
    {
        var target = FindNavigationItem(routeKey);
        if (target is not null)
        {
            SelectedItem = target;
        }
    }

    public async Task InitializeAsync()
    {
        foreach (var item in NavigationItems)
        {
            item.IsSelected = item == SelectedItem;
        }
        if (SelectedItem is not null)
        {
            await InitializeItemAsync(SelectedItem);
        }
    }

    private async Task InitializeItemAsync(NavigationItem item)
    {
        if (!_initializedViewModels.Add(item.ViewModel))
        {
            return;
        }

        if (item.ViewModel is not IInitializable initializable)
        {
            return;
        }

        try
        {
            await initializable.InitializeAsync();
        }
        catch (Exception ex)
        {
            _initializedViewModels.Remove(item.ViewModel);
            if (item.ViewModel is ViewModelBase viewModel)
            {
                viewModel.StatusMessage = $"初始化失败：{ex.Message}";
            }
        }
    }

    private NavigationItem? FindNavigationItem(string? routeKey)
    {
        return routeKey switch
        {
            "dashboard" => NavigationItems.FirstOrDefault(item => item.Title == "概览"),
            "broken-uninstall" => NavigationItems.FirstOrDefault(item => item.Title == "失效卸载条目"),
            "startup" => NavigationItems.FirstOrDefault(item => item.Title == "启动项"),
            "context-menu" => NavigationItems.FirstOrDefault(item => item.Title == "右键菜单"),
            "scheduled-task" => NavigationItems.FirstOrDefault(item => item.Title == "计划任务"),
            "service" => NavigationItems.FirstOrDefault(item => item.Title == "系统服务"),
            "history" => NavigationItems.FirstOrDefault(item => item.Title == "历史记录"),
            "registry" => NavigationItems.FirstOrDefault(item => item.Title == "注册表清理"),
            "residue" => NavigationItems.FirstOrDefault(item => item.Title == "残留清理"),
            _ => null
        };
    }
}

public interface IInitializable
{
    Task InitializeAsync();
}