using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Models;
using System.Collections.ObjectModel;

namespace SysCleaner.Wpf.ViewModels;

public sealed partial class SystemRepairViewModel(ISystemRepairService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<SystemRepairItem> Items { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunBusyAsync("正在刷新系统修复诊断", async () =>
        {
            Items.Clear();
            foreach (var item in await service.AnalyzeAsync())
            {
                Items.Add(item);
            }

            StatusMessage = "已刷新系统修复诊断。";
        });
    }

    [RelayCommand]
    private async Task RepairIconCacheAsync()
    {
        await RunBusyAsync("正在修复图标缓存", async () =>
        {
            StatusMessage = (await service.RepairIconCacheAsync()).Message;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task RepairWindowsAvatarAsync()
    {
        await RunBusyAsync("正在修复 Windows 头像缓存", async () =>
        {
            StatusMessage = (await service.RepairWindowsAvatarAsync()).Message;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task RepairItemAsync(SystemRepairItem? item)
    {
        if (item is null)
        {
            StatusMessage = "当前没有可执行的修复项。";
            return;
        }

        switch (item.Id)
        {
            case "icon-cache":
                await RepairIconCacheAsync();
                break;
            case "windows-avatar":
                await RepairWindowsAvatarAsync();
                break;
            default:
                StatusMessage = "当前修复项暂未提供直接执行入口。";
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
                $"建议: {item.Recommendation}",
                $"需重启 Explorer: {item.RequiresExplorerRestart}",
                $"建议重新登录: {item.RequiresSignOut}"
            ]),
            "已复制系统修复诊断信息。");
    }

    public Task InitializeAsync() => RefreshAsync();
}