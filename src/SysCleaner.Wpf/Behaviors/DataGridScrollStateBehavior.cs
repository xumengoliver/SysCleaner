using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SysCleaner.Wpf.Behaviors;

public static class DataGridScrollStateBehavior
{
    public static readonly DependencyProperty PreserveScrollOnItemsChangeProperty =
        DependencyProperty.RegisterAttached(
            "PreserveScrollOnItemsChange",
            typeof(bool),
            typeof(DataGridScrollStateBehavior),
            new PropertyMetadata(false, OnPreserveScrollOnItemsChangeChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ScrollPreservationState),
            typeof(DataGridScrollStateBehavior),
            new PropertyMetadata(null));

    public static bool GetPreserveScrollOnItemsChange(DependencyObject obj) =>
        (bool)obj.GetValue(PreserveScrollOnItemsChangeProperty);

    public static void SetPreserveScrollOnItemsChange(DependencyObject obj, bool value) =>
        obj.SetValue(PreserveScrollOnItemsChangeProperty, value);

    private static ScrollPreservationState? GetState(DependencyObject obj) =>
        (ScrollPreservationState?)obj.GetValue(StateProperty);

    private static void SetState(DependencyObject obj, ScrollPreservationState? value) =>
        obj.SetValue(StateProperty, value);

    private static void OnPreserveScrollOnItemsChangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

        GetState(dataGrid)?.Detach();
        SetState(dataGrid, null);

        if ((bool)e.NewValue)
        {
            SetState(dataGrid, new ScrollPreservationState(dataGrid));
        }
    }

    private sealed class ScrollPreservationState
    {
        private readonly DataGrid _dataGrid;
        private ScrollViewer? _scrollViewer;
        private INotifyCollectionChanged? _collection;
        private double _lastKnownVerticalOffset;
        private bool _restorePending;

        public ScrollPreservationState(DataGrid dataGrid)
        {
            _dataGrid = dataGrid;
            _dataGrid.Loaded += OnLoaded;
            _dataGrid.Unloaded += OnUnloaded;

            AttachToCollection();
        }

        public void Detach()
        {
            _dataGrid.Loaded -= OnLoaded;
            _dataGrid.Unloaded -= OnUnloaded;
            DetachFromCollection();

            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer = null;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            EnsureScrollViewer();
            AttachToCollection();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachFromCollection();
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer = null;
            }
        }

        private void AttachToCollection()
        {
            var newCollection = _dataGrid.ItemsSource as INotifyCollectionChanged;
            if (ReferenceEquals(_collection, newCollection))
            {
                return;
            }

            DetachFromCollection();
            _collection = newCollection;
            if (_collection is not null)
            {
                _collection.CollectionChanged += OnCollectionChanged;
            }
        }

        private void DetachFromCollection()
        {
            if (_collection is not null)
            {
                _collection.CollectionChanged -= OnCollectionChanged;
                _collection = null;
            }
        }

        private void EnsureScrollViewer()
        {
            if (_scrollViewer is not null)
            {
                return;
            }

            _scrollViewer = FindDescendant<ScrollViewer>(_dataGrid);
            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
                _lastKnownVerticalOffset = _scrollViewer.VerticalOffset;
            }
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_restorePending)
            {
                return;
            }

            _lastKnownVerticalOffset = e.VerticalOffset;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_lastKnownVerticalOffset <= 0)
            {
                return;
            }

            ScheduleRestore();
        }

        private void ScheduleRestore()
        {
            EnsureScrollViewer();
            if (_scrollViewer is null || _restorePending)
            {
                return;
            }

            _restorePending = true;
            _dataGrid.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_scrollViewer is null || _dataGrid.Items.Count == 0)
                    {
                        return;
                    }

                    var targetOffset = Math.Min(_lastKnownVerticalOffset, _scrollViewer.ScrollableHeight);
                    _scrollViewer.ScrollToVerticalOffset(targetOffset);
                }
                finally
                {
                    _restorePending = false;
                }
            }, DispatcherPriority.Background);
        }

        private static T? FindDescendant<T>(DependencyObject? current) where T : DependencyObject
        {
            if (current is null)
            {
                return null;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is T target)
                {
                    return target;
                }

                var nested = FindDescendant<T>(child);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
