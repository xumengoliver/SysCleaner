using System;
using System.Windows;
using System.Windows.Media;

namespace SysCleaner.Wpf.Views
{
    public partial class ConfirmationDialog : Window
    {
        public ConfirmationDialog(
            string title,
            string header,
            string message,
            Brush headerBrush,
            bool isConfirmation = true,
            string? confirmText = null,
            string? cancelText = null,
            string? shortcutHint = null,
            bool isDangerous = false)
        {
            InitializeComponent();
            Title = title;
            MaxHeight = SystemParameters.WorkArea.Height * 0.84;
            var tone = ResolveTone(header, isConfirmation, isDangerous);
            DataContext = new ConfirmationDialogModel(
                title,
                header,
                message,
                headerBrush,
                CreateMutedBrush(headerBrush, 0.10),
                CreateMutedBrush(headerBrush, 0.22),
                isConfirmation,
                isDangerous,
                tone.IconGlyph,
                tone.ToneLabel,
                tone.ToneDescription,
                isConfirmation ? "执行前请确认影响范围" : "当前提示",
                isConfirmation ? (isDangerous ? "该操作会直接影响系统或当前数据。" : "请确认目标与范围无误后继续。") : "这是一条统一样式的提示信息。",
                isConfirmation ? (isDangerous ? "建议先备份或关闭相关程序。" : "确认后将立即继续执行。") : "阅读完成后可直接关闭该窗口。",
                confirmText ?? (isConfirmation ? "确认" : "知道了"),
                cancelText ?? "取消",
                shortcutHint ?? (isConfirmation ? "Enter 确认 / Esc 取消" : "Enter / Esc 关闭"));

            ConfirmButton.Click += Confirm_Click;

            if (!isConfirmation)
            {
                CancelButton.Visibility = Visibility.Collapsed;
                ConfirmButton.MinWidth = 120;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private static Brush CreateMutedBrush(Brush source, double opacity)
        {
            var brush = source.CloneCurrentValue();
            brush.Opacity = opacity;
            return brush;
        }

        private static DialogTone ResolveTone(string header, bool isConfirmation, bool isDangerous)
        {
            if (!isConfirmation)
            {
                return new DialogTone("i", "信息提示", "用于展示结果、限制说明或后续步骤。", false);
            }

            if (isDangerous)
            {
                return new DialogTone("!", "高风险操作", "执行后可能删除数据、改写配置或触发外部系统行为。", true);
            }

            if (header.Contains("警告", StringComparison.OrdinalIgnoreCase))
            {
                return new DialogTone("!", "注意确认", "建议再次核对当前目标，避免误触关键操作。", false);
            }

            if (header.Contains("信息", StringComparison.OrdinalIgnoreCase))
            {
                return new DialogTone("i", "信息确认", "该操作风险较低，但仍建议确认影响范围。", false);
            }

            return new DialogTone("?", "普通确认", "该操作需要一次显式确认后才会执行。", false);
        }

        private sealed record ConfirmationDialogModel(
            string Title,
            string Header,
            string Message,
            Brush HeaderBrush,
            Brush HeaderSurfaceBrush,
            Brush HeaderOutlineBrush,
            bool IsConfirmation,
            bool IsDangerous,
            string IconGlyph,
            string ToneLabel,
            string ToneDescription,
            string MessageCaption,
            string FooterText,
            string FooterSubtext,
            string ConfirmText,
            string CancelText,
            string ShortcutHint);

        private sealed record DialogTone(string IconGlyph, string ToneLabel, string ToneDescription, bool IsDangerous);
    }
}
