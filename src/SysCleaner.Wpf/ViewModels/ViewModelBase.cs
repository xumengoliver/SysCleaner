using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using SysCleaner.Domain.Enums;
using SysCleaner.Domain.Models;
using SysCleaner.Wpf.Views;

namespace SysCleaner.Wpf.ViewModels
{
public abstract partial class ViewModelBase : ObservableObject
{
    private readonly Stack<BusyState> _busyStates = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isBlockingBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _busyMessage = "正在处理，请稍候...";

    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 100;

    protected Task RunBusyAsync(string busyMessage, Func<Task> action, bool blockUi = true)
    {
        using var _ = BeginBusyScope(busyMessage, blockUi);
        return action();
    }

    protected async Task<T> RunBusyAsync<T>(string busyMessage, Func<Task<T>> action, bool blockUi = true)
    {
        using var _ = BeginBusyScope(busyMessage, blockUi);
        return await action();
    }

    protected IDisposable BeginBusyScope(string busyMessage, bool blockUi = true)
    {
        _busyStates.Push(new BusyState(IsBusy, IsBlockingBusy, BusyMessage, IsProgressIndeterminate, ProgressValue, ProgressMaximum));
        IsBusy = true;
        IsBlockingBusy = blockUi;
        BusyMessage = busyMessage;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        ProgressMaximum = 100;
        return new BusyScope(this);
    }

    protected void ReportBusyProgress(string busyMessage, double completed, double total)
    {
        BusyMessage = busyMessage;
        ProgressMaximum = total <= 0 ? 1 : total;
        ProgressValue = Math.Max(0, Math.Min(completed, ProgressMaximum));
        IsProgressIndeterminate = false;
    }

    protected bool CopyTextToClipboard(string? text, string successMessage, string emptyMessage = "没有可复制的内容。")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = emptyMessage;
            return false;
        }

        Clipboard.SetText(text);
        StatusMessage = successMessage;
        return true;
    }

    protected bool ConfirmAction(string title, string message, MessageBoxImage icon = MessageBoxImage.Warning)
    {
        var dialog = new ConfirmationDialog(
            title,
            BuildConfirmationHeader(icon),
            message,
            ResolveConfirmationBrush(icon),
            isConfirmation: true,
            confirmText: BuildConfirmButtonText(title),
            shortcutHint: "Enter 确认 / Esc 取消",
            isDangerous: icon is MessageBoxImage.Warning or MessageBoxImage.Error);
        var owner = WpfApplication.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? WpfApplication.Current?.MainWindow;

        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }

    protected void ShowNotice(string title, string message, MessageBoxImage icon = MessageBoxImage.Information)
    {
        var dialog = new ConfirmationDialog(
            title,
            BuildNoticeHeader(icon),
            message,
            ResolveConfirmationBrush(icon),
            isConfirmation: false,
            confirmText: "知道了",
            shortcutHint: "Enter / Esc 关闭");
        var owner = WpfApplication.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? WpfApplication.Current?.MainWindow;

        if (owner is not null && owner != dialog)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }

    protected bool ConfirmDangerousAction(
        string actionName,
        string targetName,
        IEnumerable<string>? details = null,
        string? caution = null,
        MessageBoxImage icon = MessageBoxImage.Error)
    {
        var lines = new List<string>
        {
            $"确认执行“{actionName}”吗？",
            string.Empty,
            $"目标：{targetName}"
        };

        if (details is not null)
        {
            lines.AddRange(details.Where(detail => !string.IsNullOrWhiteSpace(detail)));
        }

        if (!string.IsNullOrWhiteSpace(caution))
        {
            lines.Add(string.Empty);
            lines.Add(caution);
        }

        return ConfirmAction(actionName, string.Join(Environment.NewLine, lines), icon);
    }

    private static string BuildConfirmButtonText(string actionName)
    {
        if (actionName.Contains("删除", StringComparison.OrdinalIgnoreCase))
        {
            return "立即删除";
        }

        if (actionName.Contains("卸载", StringComparison.OrdinalIgnoreCase))
        {
            return "立即卸载";
        }

        if (actionName.Contains("关闭", StringComparison.OrdinalIgnoreCase))
        {
            return "立即关闭";
        }

        if (actionName.Contains("替换", StringComparison.OrdinalIgnoreCase))
        {
            return "立即替换";
        }

        if (actionName.Contains("编辑", StringComparison.OrdinalIgnoreCase) || actionName.Contains("写入", StringComparison.OrdinalIgnoreCase))
        {
            return "立即写入";
        }

        if (actionName.Contains("清理", StringComparison.OrdinalIgnoreCase))
        {
            return "立即清理";
        }

        return "继续执行";
    }

    private static Brush ResolveConfirmationBrush(MessageBoxImage icon)
    {
        return icon switch
        {
            MessageBoxImage.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626")),
            MessageBoxImage.Information => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
            MessageBoxImage.Question => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F766E")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"))
        };
    }

    private static string BuildConfirmationHeader(MessageBoxImage icon)
    {
        return icon switch
        {
            MessageBoxImage.Error => "高风险确认",
            MessageBoxImage.Information => "信息确认",
            MessageBoxImage.Question => "普通确认",
            _ => "警告确认"
        };
    }

    private static string BuildNoticeHeader(MessageBoxImage icon)
    {
        return icon switch
        {
            MessageBoxImage.Error => "错误提示",
            MessageBoxImage.Question => "操作提示",
            MessageBoxImage.Warning => "警告提示",
            _ => "信息提示"
        };
    }

    private void EndBusyScope()
    {
        if (_busyStates.Count == 0)
        {
            IsBusy = false;
            IsBlockingBusy = false;
            BusyMessage = "正在处理，请稍候...";
            IsProgressIndeterminate = true;
            ProgressValue = 0;
            ProgressMaximum = 100;
            return;
        }

        var previous = _busyStates.Pop();
        IsBusy = previous.IsBusy;
        IsBlockingBusy = previous.IsBlockingBusy;
        BusyMessage = previous.BusyMessage;
        IsProgressIndeterminate = previous.IsProgressIndeterminate;
        ProgressValue = previous.ProgressValue;
        ProgressMaximum = previous.ProgressMaximum;
    }

    partial void OnIsBusyChanged(bool value) => OnBusyStateChanged();

    protected virtual void OnBusyStateChanged()
    {
    }

    private readonly record struct BusyState(bool IsBusy, bool IsBlockingBusy, string BusyMessage, bool IsProgressIndeterminate, double ProgressValue, double ProgressMaximum);

    private sealed class BusyScope(ViewModelBase owner) : IDisposable
    {
        private ViewModelBase? _owner = owner;

        public void Dispose()
        {
            _owner?.EndBusyScope();
            _owner = null;
        }
    }
}

public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem(string section, string title, string description, object viewModel)
    {
        Section = section;
        Title = title;
        Description = description;
        ViewModel = viewModel;
    }

    public string Section { get; }
    public string Title { get; }
    public string Description { get; }
    public object ViewModel { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed class BooleanToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isSelected = value is true;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(isSelected ? "#DCEFEB" : "#F8FBFC"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }
}

public sealed class RegistryGroupRiskDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var source = value?.ToString() ?? string.Empty;
        if (source.Contains("MuiCache", StringComparison.OrdinalIgnoreCase)
            || source.Contains("SharedDlls", StringComparison.OrdinalIgnoreCase)
            || source.Contains("失效卸载条目", StringComparison.OrdinalIgnoreCase))
        {
            return "通常可安全清理，属于缓存、引用计数或已失效卸载记录。";
        }

        if (source.Contains("协议处理器", StringComparison.OrdinalIgnoreCase)
            || source.Contains("文件关联命令", StringComparison.OrdinalIgnoreCase)
            || source.Contains("右键命令", StringComparison.OrdinalIgnoreCase))
        {
            return "建议确认是否仍需保留关联动作；目标程序缺失时一般可以清理。";
        }

        if (source.Contains("Shell 扩展", StringComparison.OrdinalIgnoreCase)
            || source.Contains("BHO", StringComparison.OrdinalIgnoreCase))
        {
            return "建议优先清理已缺失组件的扩展项，但若来自系统目录应再次确认。";
        }

        return "优先展示目标明确缺失的高置信度项，删除前仍建议核对来源。";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class RegistryConfidenceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CleanupCandidate candidate)
        {
            return string.Empty;
        }

        if (candidate.Health == ItemHealth.Broken
            && candidate.Risk == RiskLevel.Safe
            && candidate.Evidence.Contains("不存在", StringComparison.OrdinalIgnoreCase))
        {
            return "高";
        }

        if (candidate.Health == ItemHealth.Broken || candidate.Risk == RiskLevel.Review)
        {
            return "中";
        }

        return "低";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class LockKindBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            LockKind.Process => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7")),
            LockKind.Service => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7")),
            LockKind.Shell => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
            LockKind.Protected => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class RiskLevelBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            RiskLevel.Safe => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7")),
            RiskLevel.Review => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE")),
            RiskLevel.High => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7")),
            RiskLevel.Protected => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class LockRecommendedActionTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LockInfo item)
        {
            return "查看建议";
        }

        if (item.Kind == LockKind.Shell)
        {
            return "重启资源管理器";
        }

        if (item.CanStopService)
        {
            return "停止服务";
        }

        if (item.CanTerminate)
        {
            return "关闭进程";
        }

        return item.Kind switch
        {
            LockKind.Service => "查看服务建议",
            LockKind.Protected => "查看保护提示",
            _ => "查看建议"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class LockRecommendedActionGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not LockInfo item)
        {
            return "\uE946";
        }

        if (item.Kind == LockKind.Shell)
        {
            return "\uE72C";
        }

        if (item.CanStopService)
        {
            return "\uE895";
        }

        if (item.CanTerminate)
        {
            return "\uE74D";
        }

        return item.Kind switch
        {
            LockKind.Service => "\uE895",
            LockKind.Protected => "\uE72E",
            _ => "\uE946"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}
}