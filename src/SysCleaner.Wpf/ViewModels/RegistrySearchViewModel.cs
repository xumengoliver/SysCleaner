using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SysCleaner.Contracts.Interfaces;
using SysCleaner.Domain.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;

namespace SysCleaner.Wpf.ViewModels;

public sealed partial class RegistrySearchItemViewModel : ObservableObject
{
    public RegistrySearchItemViewModel(RegistrySearchResult result)
    {
        Result = result;
    }

    public RegistrySearchResult Result { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class RegistrySearchViewModel(IRegistryCleanupService service) : ViewModelBase, IInitializable
{
    public ObservableCollection<RegistrySearchItemViewModel> Items { get; } = [];
    private readonly List<RegistrySearchItemViewModel> _allItems = [];

    [ObservableProperty]
    private RegistrySearchItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _newValue = string.Empty;

    [ObservableProperty]
    private string _replaceOldValue = string.Empty;

    [ObservableProperty]
    private string _replaceNewValue = string.Empty;

    [ObservableProperty]
    private bool _exportSelectedOnly;

    [ObservableProperty]
    private bool _replaceMatchCase;

    [ObservableProperty]
    private bool _replaceWholeWord;

    [ObservableProperty]
    private bool _searchKeyPath = true;

    [ObservableProperty]
    private bool _searchValueName = true;

    [ObservableProperty]
    private bool _searchValueData = true;

    [ObservableProperty]
    private bool _showOnlyEditable;

    [ObservableProperty]
    private bool _showOnlyDeletable;

    [RelayCommand]
    private async Task SearchAsync()
    {
        var keyword = Query.Trim();
        if (keyword.Length < 2)
        {
            StatusMessage = "搜索关键字至少需要 2 个字符。";
            return;
        }

        await RunBusyAsync($"正在搜索注册表：{keyword}", async () =>
        {
            ClearTrackedItems();
            Items.Clear();
            _allItems.Clear();
            foreach (var result in await service.SearchAsync(new RegistrySearchOptions(keyword, SearchKeyPath, SearchValueName, SearchValueData)))
            {
                var item = new RegistrySearchItemViewModel(result);
                item.PropertyChanged += OnItemPropertyChanged;
                _allItems.Add(item);
            }

            ApplyFilters();
            StatusMessage = $"已找到 {_allItems.Count} 个注册表搜索结果，当前显示 {Items.Count} 个。";
            RefreshSelectionCommands();
        });
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }

        StatusMessage = $"已选择 {Items.Count} 个结果。";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }

        StatusMessage = "已清空选择。";
    }

    [RelayCommand(CanExecute = nameof(CanSelectCurrent))]
    private void SelectCurrent()
    {
        if (SelectedItem is null)
        {
            return;
        }

        SelectedItem.IsSelected = true;
        StatusMessage = "已选中当前注册表结果。";
        RefreshSelectionCommands();
    }

    [RelayCommand(CanExecute = nameof(CanSelectCurrent))]
    private void SelectOnlyCurrent()
    {
        if (SelectedItem is null)
        {
            return;
        }

        foreach (var item in Items)
        {
            item.IsSelected = item == SelectedItem;
        }

        StatusMessage = "已仅保留当前注册表结果为选中状态。";
        RefreshSelectionCommands();
    }

    [RelayCommand]
    private void ExportResults()
    {
        var exportItems = ExportSelectedOnly
            ? Items.Where(item => item.IsSelected).ToList()
            : Items.ToList();

        if (exportItems.Count == 0)
        {
            StatusMessage = ExportSelectedOnly ? "当前没有已选中的可导出结果。" : "当前没有可导出的搜索结果。";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"registry-search-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string>
        {
            "EntryType,MatchTarget,HiveName,KeyPath,ValueName,ValueData,ValueKind,CanEdit,CanDelete"
        };

        foreach (var item in exportItems)
        {
            lines.Add(string.Join(",",
            [
                EscapeCsv(item.Result.EntryType),
                EscapeCsv(item.Result.MatchTarget),
                EscapeCsv(item.Result.HiveName),
                EscapeCsv(item.Result.KeyPath),
                EscapeCsv(item.Result.ValueName),
                EscapeCsv(item.Result.ValueData),
                EscapeCsv(item.Result.ValueKind),
                EscapeCsv(item.Result.CanEdit.ToString()),
                EscapeCsv(item.Result.CanDelete.ToString())
            ]));
        }

        File.WriteAllLines(dialog.FileName, lines, Encoding.UTF8);
        StatusMessage = $"已导出 {exportItems.Count} 个搜索结果到 {dialog.FileName}";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        var selected = Items.Where(item => item.IsSelected).Select(item => item.Result).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        if (!ConfirmDangerousAction(
                "批量删除注册表结果",
                $"{selected.Count} 个搜索结果",
                [
                    $"关键字：{Query}",
                    $"仅当前显示结果：{Items.Count} 项"
                ],
                "删除前建议再次核对根键、键路径和值名称，避免跨分支误删。"))
        {
            return;
        }

        await RunBusyAsync($"正在批量删除 {selected.Count} 个注册表项", async () =>
        {
            StatusMessage = (await service.DeleteSearchResultsAsync(selected)).Message;
            await SearchAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteCurrent))]
    private async Task DeleteCurrentAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (!ConfirmDangerousAction(
                "删除注册表结果",
                SelectedItem.Result.ValueName.Length == 0
                    ? $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}"
                    : $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}\\{SelectedItem.Result.ValueName}",
                [
                    $"命中类型：{SelectedItem.Result.MatchTarget}",
                    $"值数据：{SelectedItem.Result.ValueData}"
                ],
                "删除前建议再次确认该项确实属于残留或无效项。"))
        {
            return;
        }

        await RunBusyAsync("正在删除当前注册表结果", async () =>
        {
            StatusMessage = (await service.DeleteSearchResultsAsync([SelectedItem.Result])).Message;
            await SearchAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanUpdateSelected))]
    private async Task UpdateSelectedAsync()
    {
        var selected = Items.Where(item => item.IsSelected).Select(item => item.Result).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        if (!ConfirmDangerousAction(
                "批量编辑注册表结果",
                $"{selected.Count} 个可编辑结果",
                [
                    $"关键字：{Query}",
                    $"新值：{NewValue}",
                    $"仅当前显示结果：{Items.Count} 项"
                ],
                "批量编辑会直接写回注册表值，建议先确认值类型和命中范围。"))
        {
            return;
        }

        await RunBusyAsync($"正在批量编辑 {selected.Count} 个注册表值", async () =>
        {
            StatusMessage = (await service.UpdateSearchResultsAsync(selected, NewValue)).Message;
            await SearchAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanUpdateCurrent))]
    private async Task UpdateCurrentAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (!ConfirmDangerousAction(
                "编辑注册表结果",
                SelectedItem.Result.ValueName.Length == 0
                    ? $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}"
                    : $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}\\{SelectedItem.Result.ValueName}",
                [
                    $"新值：{NewValue}",
                    $"当前值：{SelectedItem.Result.ValueData}"
                ],
                "写入前建议确认该值支持编辑，且新内容符合目标程序预期。"))
        {
            return;
        }

        await RunBusyAsync("正在编辑当前注册表值", async () =>
        {
            StatusMessage = (await service.UpdateSearchResultsAsync([SelectedItem.Result], NewValue)).Message;
            await SearchAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanReplaceSelected))]
    private async Task ReplaceSelectedAsync()
    {
        var selected = Items.Where(item => item.IsSelected).Select(item => item.Result).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ReplaceOldValue))
        {
            StatusMessage = "替换前内容不能为空。";
            return;
        }

        if (!ConfirmDangerousAction(
                "批量替换注册表结果",
                $"{selected.Count} 个选中结果",
                [
                    $"查找内容：{ReplaceOldValue}",
                    $"替换内容：{ReplaceNewValue}",
                    $"区分大小写：{ReplaceMatchCase}",
                    $"全词匹配：{ReplaceWholeWord}"
                ],
                "批量替换会直接修改命中的值数据，建议先缩小筛选范围再执行。"))
        {
            return;
        }

        await RunBusyAsync($"正在批量替换 {selected.Count} 个注册表值", async () =>
        {
            StatusMessage = (await service.ReplaceInSearchResultsAsync(selected, new RegistryReplaceOptions(ReplaceOldValue, ReplaceNewValue, ReplaceMatchCase, ReplaceWholeWord))).Message;
            await SearchAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanReplaceCurrent))]
    private async Task ReplaceCurrentAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ReplaceOldValue))
        {
            StatusMessage = "替换前内容不能为空。";
            return;
        }

        if (!ConfirmDangerousAction(
                "替换注册表结果",
                SelectedItem.Result.ValueName.Length == 0
                    ? $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}"
                    : $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}\\{SelectedItem.Result.ValueName}",
                [
                    $"查找内容：{ReplaceOldValue}",
                    $"替换内容：{ReplaceNewValue}",
                    $"当前值：{SelectedItem.Result.ValueData}"
                ],
                "替换前建议确认该值确实属于当前软件残留或无效配置。"))
        {
            return;
        }

        await RunBusyAsync("正在替换当前注册表值", async () =>
        {
            StatusMessage = (await service.ReplaceInSearchResultsAsync([SelectedItem.Result], new RegistryReplaceOptions(ReplaceOldValue, ReplaceNewValue, ReplaceMatchCase, ReplaceWholeWord))).Message;
            await SearchAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanSelectCurrent))]
    private void CopySelectedLocation()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var location = string.IsNullOrWhiteSpace(SelectedItem.Result.ValueName)
            ? $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}"
            : $"{SelectedItem.Result.HiveName}\\{SelectedItem.Result.KeyPath}\\{SelectedItem.Result.ValueName}";

        CopyTextToClipboard(location, "已复制注册表结果位置。");
    }

    private bool CanDeleteSelected() => Items.Any(item => item.IsSelected && item.Result.CanDelete);

    private bool CanUpdateSelected() => Items.Any(item => item.IsSelected && item.Result.CanEdit);

    private bool CanReplaceSelected() => Items.Any(item => item.IsSelected && item.Result.CanEdit);

    private bool CanSelectCurrent() => SelectedItem is not null;

    private bool CanDeleteCurrent() => SelectedItem?.Result.CanDelete is true;

    private bool CanUpdateCurrent() => SelectedItem?.Result.CanEdit is true;

    private bool CanReplaceCurrent() => SelectedItem?.Result.CanEdit is true;

    partial void OnShowOnlyEditableChanged(bool value) => ApplyFilters();

    partial void OnShowOnlyDeletableChanged(bool value) => ApplyFilters();

    public Task InitializeAsync() => Task.CompletedTask;

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RegistrySearchItemViewModel.IsSelected))
        {
            RefreshSelectionCommands();
        }
    }

    private void RefreshSelectionCommands()
    {
        SelectCurrentCommand.NotifyCanExecuteChanged();
        SelectOnlyCurrentCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        DeleteCurrentCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
        UpdateCurrentCommand.NotifyCanExecuteChanged();
        ReplaceSelectedCommand.NotifyCanExecuteChanged();
        ReplaceCurrentCommand.NotifyCanExecuteChanged();
        CopySelectedLocationCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedItemChanged(RegistrySearchItemViewModel? value) => RefreshSelectionCommands();

    private void ApplyFilters()
    {
        Items.Clear();
        IEnumerable<RegistrySearchItemViewModel> filtered = _allItems;

        if (ShowOnlyEditable)
        {
            filtered = filtered.Where(item => item.Result.CanEdit);
        }

        if (ShowOnlyDeletable)
        {
            filtered = filtered.Where(item => item.Result.CanDelete);
        }

        foreach (var item in filtered)
        {
            Items.Add(item);
        }

        RefreshSelectionCommands();
    }

    private void ClearTrackedItems()
    {
        foreach (var item in _allItems)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
    }

    private static string EscapeCsv(string value)
    {
        var normalized = value.Replace("\"", "\"\"");
        return $"\"{normalized}\"";
    }
}