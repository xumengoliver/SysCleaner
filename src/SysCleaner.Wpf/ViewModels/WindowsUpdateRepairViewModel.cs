using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace SysCleaner.Wpf.ViewModels;

public sealed partial class WindowsUpdateRepairViewModel(IWindowsUpdateRepairService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<SystemRepairItem> Items { get; } = [];
    public ObservableCollection<WindowsInstalledUpdate> InstalledUpdates { get; } = [];
    public ObservableCollection<WindowsUpdateEventRecord> RecentFailures { get; } = [];
    public ObservableCollection<WindowsUpdateFailureGroup> FailureGroups { get; } = [];
    private readonly List<WindowsUpdateEventRecord> _allFailures = [];

    [ObservableProperty]
    private WindowsInstalledUpdate? _selectedInstalledUpdate;

    [ObservableProperty]
    private WindowsUpdateEventRecord? _selectedFailure;

    [ObservableProperty]
    private WindowsUpdateFailureGroup? _selectedFailureGroup;

    [ObservableProperty]
    private string _lastSuccessfulInstallText = "未检测到最近成功安装记录";

    [ObservableProperty]
    private string _pendingRebootText = "待重启状态：否";

    [ObservableProperty]
    private string _failureFilterText = "错误筛选：全部";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在刷新系统更新诊断", async () =>
        {
            Items.Clear();
            InstalledUpdates.Clear();
            RecentFailures.Clear();
            FailureGroups.Clear();
            _allFailures.Clear();
            SelectedFailureGroup = null;

            var diagnosticsTask = service.AnalyzeAsync();
            var overviewTask = service.GetOverviewAsync();
            await Task.WhenAll(diagnosticsTask, overviewTask);

            foreach (var item in diagnosticsTask.Result)
            {
                Items.Add(item);
            }

            foreach (var item in overviewTask.Result.InstalledUpdates)
            {
                InstalledUpdates.Add(item);
            }

            foreach (var item in overviewTask.Result.RecentFailures)
            {
                _allFailures.Add(item);
            }

            foreach (var group in overviewTask.Result.RecentFailures
                         .GroupBy(item => string.IsNullOrWhiteSpace(item.ErrorCode) ? "未识别错误码" : item.ErrorCode)
                         .Select(group => new WindowsUpdateFailureGroup(
                             group.Key,
                             group.Count(),
                             group.Max(item => item.Timestamp),
                             group.OrderByDescending(item => item.Timestamp).Select(item => item.Title).FirstOrDefault() ?? string.Empty))
                         .OrderByDescending(group => group.Count)
                         .ThenByDescending(group => group.LatestTime))
            {
                FailureGroups.Add(group);
            }

            ApplyFailureFilter();

            PendingRebootText = overviewTask.Result.PendingReboot ? "待重启状态：是，系统存在更新重启要求" : "待重启状态：否";
            LastSuccessfulInstallText = overviewTask.Result.LastSuccessfulInstallTime.HasValue
                ? $"最近成功更新：{overviewTask.Result.LastSuccessfulInstallTime:yyyy-MM-dd HH:mm} {overviewTask.Result.LastSuccessfulInstallTitle}"
                : "未检测到最近成功安装记录";

            StatusMessage = "已刷新系统更新诊断。";
        });
    }

    [RelayCommand(CanExecute = nameof(CanUninstallSelectedUpdate))]
    private async Task UninstallSelectedUpdateAsync()
    {
        if (SelectedInstalledUpdate is null)
        {
            return;
        }

        if (!ConfirmDangerousAction(
                "卸载 Windows 更新",
                SelectedInstalledUpdate.KbId,
                [
                    $"说明：{SelectedInstalledUpdate.Title}",
                    $"类别：{SelectedInstalledUpdate.UpdateType}",
                    $"安装时间：{SelectedInstalledUpdate.InstalledOn:yyyy-MM-dd HH:mm:ss}"
                ],
                "卸载完成后通常建议重启系统。"))
        {
            return;
        }

        await RunBusyAsync($"正在启动 {SelectedInstalledUpdate.KbId} 的卸载流程", async () =>
        {
            var result = await service.UninstallUpdateAsync(SelectedInstalledUpdate);
            StatusMessage = result.Success ? result.Message + " 如系统提示，请在卸载完成后重启。" : result.Message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanCopyFailureDetail))]
    private Task CopyFailureDetailAsync()
    {
        if (SelectedFailure is null)
        {
            return Task.CompletedTask;
        }

        var content = string.Join(Environment.NewLine,
        [
            $"时间: {SelectedFailure.Timestamp:yyyy-MM-dd HH:mm:ss}",
            $"标题: {SelectedFailure.Title}",
            $"结果: {SelectedFailure.Result}",
            $"错误码: {SelectedFailure.ErrorCode}",
            "详情:",
            SelectedFailure.Details
        ]);

        Clipboard.SetText(content);
        StatusMessage = "已复制失败详情。";
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanCopySelectedInstalledUpdate))]
    private void CopyInstalledUpdate()
    {
        if (SelectedInstalledUpdate is null)
        {
            return;
        }

        CopyTextToClipboard(
            string.Join(Environment.NewLine,
            [
                $"KB: {SelectedInstalledUpdate.KbId}",
                $"说明: {SelectedInstalledUpdate.Title}",
                $"类别: {SelectedInstalledUpdate.UpdateType}",
                $"安全更新: {SelectedInstalledUpdate.IsSecurityUpdate}",
                $"安装时间: {SelectedInstalledUpdate.InstalledOn:yyyy-MM-dd HH:mm:ss}",
                $"安装者: {SelectedInstalledUpdate.InstalledBy}"
            ]),
            "已复制已安装更新信息。");
    }

    [RelayCommand(CanExecute = nameof(CanCopySelectedFailureGroup))]
    private void CopyFailureGroup()
    {
        if (SelectedFailureGroup is null)
        {
            return;
        }

        CopyTextToClipboard(
            string.Join(Environment.NewLine,
            [
                $"错误码: {SelectedFailureGroup.ErrorCode}",
                $"出现次数: {SelectedFailureGroup.Count}",
                $"最近时间: {SelectedFailureGroup.LatestTime:yyyy-MM-dd HH:mm:ss}",
                $"最近标题: {SelectedFailureGroup.LatestTitle}"
            ]),
            "已复制错误码聚合摘要。");
    }

    [RelayCommand]
    private async Task RunDiagnosticActionAsync(SystemRepairItem? item)
    {
        if (item is null)
        {
            StatusMessage = "当前没有可执行的诊断项。";
            return;
        }

        switch (item.Id)
        {
            case "windows-update-services":
                await RestartCoreServicesAsync();
                break;
            case "windows-update-cache":
                await ResetWindowsUpdateComponentsAsync();
                break;
            case "windows-component-store":
                await RunDismRestoreHealthAsync();
                break;
            default:
                StatusMessage = "当前诊断项暂未提供直接执行入口。";
                break;
        }
    }

    [RelayCommand]
    private void CopyDiagnostic(SystemRepairItem? item)
    {
        if (item is null)
        {
            StatusMessage = "当前没有可复制的诊断项。";
            return;
        }

        CopyTextToClipboard(
            string.Join(Environment.NewLine,
            [
                $"项目: {item.Title}",
                $"状态: {item.Health}",
                $"说明: {item.Description}",
                $"检测结果: {item.DetectionSummary}",
                $"建议: {item.Recommendation}"
            ]),
            "已复制系统更新诊断信息。");
    }

    [RelayCommand]
    private void ClearFailureFilter()
    {
        SelectedFailureGroup = null;
        ApplyFailureFilter();
    }

    [RelayCommand]
    private async Task RestartCoreServicesAsync()
    {
        await RunBusyAsync("正在重启 Windows Update 核心服务", async () =>
        {
            StatusMessage = (await service.RestartCoreServicesAsync()).Message;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task ResetWindowsUpdateComponentsAsync()
    {
        await RunBusyAsync("正在重置 Windows Update 组件", async () =>
        {
            StatusMessage = (await service.ResetWindowsUpdateComponentsAsync()).Message;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task RunDismRestoreHealthAsync()
    {
        await RunBusyAsync("正在运行 DISM RestoreHealth", async () =>
        {
            StatusMessage = (await service.RunDismRestoreHealthAsync()).Message;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task RunSfcScanAsync()
    {
        await RunBusyAsync("正在运行 SFC 系统扫描", async () =>
        {
            StatusMessage = (await service.RunSfcScanAsync()).Message;
            await RefreshAsync();
        });
    }

    private bool CanUninstallSelectedUpdate() => SelectedInstalledUpdate is not null && SelectedInstalledUpdate.CanUninstall;

    private bool CanCopyFailureDetail() => SelectedFailure is not null;

    private bool CanCopySelectedInstalledUpdate() => SelectedInstalledUpdate is not null;

    private bool CanCopySelectedFailureGroup() => SelectedFailureGroup is not null;

    partial void OnSelectedInstalledUpdateChanged(WindowsInstalledUpdate? value)
    {
        UninstallSelectedUpdateCommand.NotifyCanExecuteChanged();
        CopyInstalledUpdateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFailureChanged(WindowsUpdateEventRecord? value) => CopyFailureDetailCommand.NotifyCanExecuteChanged();

    partial void OnSelectedFailureGroupChanged(WindowsUpdateFailureGroup? value)
    {
        CopyFailureGroupCommand.NotifyCanExecuteChanged();
        ApplyFailureFilter();
    }

    public Task InitializeAsync() => RefreshAsync();

    private void ApplyFailureFilter()
    {
        RecentFailures.Clear();
        var filtered = SelectedFailureGroup is null
            ? _allFailures
            : _allFailures.Where(item => (string.IsNullOrWhiteSpace(item.ErrorCode) ? "未识别错误码" : item.ErrorCode) == SelectedFailureGroup.ErrorCode).ToList();

        foreach (var item in filtered)
        {
            RecentFailures.Add(item);
        }

        FailureFilterText = SelectedFailureGroup is null ? "错误筛选：全部" : $"错误筛选：{SelectedFailureGroup.ErrorCode}";
    }
}