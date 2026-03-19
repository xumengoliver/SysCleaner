using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace SysCleaner.Wpf.Behaviors
{
public sealed class FileDropBehavior : DependencyObject
{
    private FileDropBehavior()
    {
    }

    public static readonly DependencyProperty DropCommandProperty =
        DependencyProperty.RegisterAttached(
            "DropCommand",
            typeof(ICommand),
            typeof(FileDropBehavior),
            new PropertyMetadata(null, OnDropCommandChanged));

    public static readonly DependencyProperty IsDragActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsDragActive",
            typeof(bool),
            typeof(FileDropBehavior),
            new PropertyMetadata(false));

    public static ICommand? GetDropCommand(DependencyObject obj) =>
        (ICommand?)obj.GetValue(DropCommandProperty);

    public static void SetDropCommand(DependencyObject obj, ICommand? value) =>
        obj.SetValue(DropCommandProperty, value);

    public static bool GetIsDragActive(DependencyObject obj) =>
        (bool)obj.GetValue(IsDragActiveProperty);

    public static void SetIsDragActive(DependencyObject obj, bool value) =>
        obj.SetValue(IsDragActiveProperty, value);

    private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (e.OldValue is null && e.NewValue is not null)
        {
            element.AllowDrop = true;
            element.PreviewDragEnter += OnPreviewDragEnter;
            element.PreviewDragOver += OnPreviewDragOver;
            element.PreviewDragLeave += OnPreviewDragLeave;
            element.Drop += OnDrop;
            return;
        }

        if (e.OldValue is not null && e.NewValue is null)
        {
            element.PreviewDragEnter -= OnPreviewDragEnter;
            element.PreviewDragOver -= OnPreviewDragOver;
            element.PreviewDragLeave -= OnPreviewDragLeave;
            element.Drop -= OnDrop;
            element.AllowDrop = false;
            if (d is DependencyObject dependencyObject)
            {
                SetIsDragActive(dependencyObject, false);
            }
        }
    }

    private static void OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject target)
        {
            return;
        }

        SetIsDragActive(target, ExtractPaths(e.Data).Length > 0);
    }

    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        var paths = ExtractPaths(e.Data);
        e.Effects = paths.Length == 0 ? DragDropEffects.None : DragDropEffects.Copy;
        if (sender is DependencyObject target)
        {
            SetIsDragActive(target, paths.Length > 0);
        }
        e.Handled = true;
    }

    private static void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject target)
        {
            SetIsDragActive(target, false);
        }
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject target)
        {
            return;
        }

        SetIsDragActive(target, false);

        var paths = ExtractPaths(e.Data);
        var command = GetDropCommand(target);
        if (paths.Length > 0 && command?.CanExecute(paths) is true)
        {
            command.Execute(paths);
            e.Handled = true;
        }
    }

    private static string[] ExtractPaths(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return (data.GetData(DataFormats.FileDrop) as string[])
            ?.Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray()
            ?? [];
    }
}
}