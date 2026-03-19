using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SysCleaner.Wpf.Behaviors
{
public static class DataGridRowSelectionBehavior
{
    public static readonly DependencyProperty EnableSelectionOnRightClickProperty =
        DependencyProperty.RegisterAttached(
            "EnableSelectionOnRightClick",
            typeof(bool),
            typeof(DataGridRowSelectionBehavior),
            new PropertyMetadata(false, OnEnableSelectionOnRightClickChanged));

    public static readonly DependencyProperty DoubleClickCommandProperty =
        DependencyProperty.RegisterAttached(
            "DoubleClickCommand",
            typeof(ICommand),
            typeof(DataGridRowSelectionBehavior),
            new PropertyMetadata(null, OnDoubleClickCommandChanged));

    public static bool GetEnableSelectionOnRightClick(DependencyObject obj) =>
        (bool)obj.GetValue(EnableSelectionOnRightClickProperty);

    public static void SetEnableSelectionOnRightClick(DependencyObject obj, bool value) =>
        obj.SetValue(EnableSelectionOnRightClickProperty, value);

    public static ICommand? GetDoubleClickCommand(DependencyObject obj) =>
        (ICommand?)obj.GetValue(DoubleClickCommandProperty);

    public static void SetDoubleClickCommand(DependencyObject obj, ICommand? value) =>
        obj.SetValue(DoubleClickCommandProperty, value);

    private static void OnEnableSelectionOnRightClickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            dataGrid.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
        }
        else
        {
            dataGrid.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
        }
    }

    private static void OnDoubleClickCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

        if (e.OldValue is null && e.NewValue is not null)
        {
            dataGrid.MouseDoubleClick += OnMouseDoubleClick;
            return;
        }

        if (e.OldValue is not null && e.NewValue is null)
        {
            dataGrid.MouseDoubleClick -= OnMouseDoubleClick;
        }
    }

    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();

        var firstColumn = dataGrid.Columns.FirstOrDefault();
        if (firstColumn is not null)
        {
            dataGrid.CurrentCell = new DataGridCellInfo(row.Item, firstColumn);
        }
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        row.IsSelected = true;
        row.Focus();

        var command = GetDoubleClickCommand(dataGrid);
        if (command?.CanExecute(null) is true)
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
}
