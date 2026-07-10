using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Utilities;
using OrderTracker.Desktop.ViewModels;

namespace OrderTracker.Desktop;

public partial class MainWindow : Window
{
    private const int DwmAttributeUseImmersiveDarkMode = 20;
    private const int DwmAttributeBorderColor = 34;
    private const int DwmAttributeCaptionColor = 35;
    private const int DwmAttributeTextColor = 36;

    private readonly MainViewModel _viewModel = new();
    private readonly List<ScrollRailBinding> _scrollRailBindings = new();
    private readonly List<ResponsiveDataGridBinding> _responsiveGridBindings = new();
    private bool _scrollRailBindingsInitialized;
    private bool _responsiveGridBindingsInitialized;
    private bool _isCommandRequeryQueued;
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;

    public static readonly RoutedUICommand SelectHighlightedRowsCommand = new(
        "Select",
        nameof(SelectHighlightedRowsCommand),
        typeof(MainWindow));

    public static readonly RoutedUICommand UnselectHighlightedRowsCommand = new(
        "Unselect",
        nameof(UnselectHighlightedRowsCommand),
        typeof(MainWindow));

    public static readonly RoutedUICommand CopyHighlightedTrackingNumbersCommand = new(
        "Copy tracking numbers",
        nameof(CopyHighlightedTrackingNumbersCommand),
        typeof(MainWindow));

    public static readonly RoutedUICommand FocusSearchCommand = new(
        "Focus search",
        nameof(FocusSearchCommand),
        typeof(MainWindow),
        new InputGestureCollection { new KeyGesture(Key.F, ModifierKeys.Control) });

    private static readonly Dictionary<string, (string Light, string Dark, string Oled)> ThemeBrushes = new()
    {
        ["AppBackgroundBrush"] = ("#F3F6FA", "#101113", "#000000"),
        ["SidebarBrush"] = ("#FFFFFF", "#15171A", "#020202"),
        ["SurfaceBrush"] = ("#FFFFFF", "#1A1D21", "#070707"),
        ["SurfaceAltBrush"] = ("#EEF3F8", "#22262B", "#101010"),
        ["ControlBrush"] = ("#F7FAFD", "#13161A", "#050505"),
        ["ControlHoverBrush"] = ("#E8F1F8", "#262B31", "#161616"),
        ["BorderBrush"] = ("#CBD7E3", "#343A42", "#303030"),
        ["TextBrush"] = ("#111827", "#F1F3F5", "#F4F4F4"),
        ["MutedTextBrush"] = ("#516174", "#A5ADB7", "#A2A2A2"),
        ["AccentBrush"] = ("#0078B8", "#58B9F6", "#00AEEF"),
        ["AccentDarkBrush"] = ("#D8F0FB", "#1D4D6F", "#003B5C"),
        ["SuccessBrush"] = ("#168765", "#2F9E7E", "#33D69F"),
        ["DangerBrush"] = ("#C93D4B", "#E05D5D", "#FF6B6B"),
        ["DangerSurfaceBrush"] = ("#FBE4E7", "#4C2529", "#2A0E12"),
        ["DataGridHeaderBrush"] = ("#EAF0F6", "#181A1E", "#050505"),
        ["DataGridAlternateBrush"] = ("#F7FAFD", "#181B1F", "#090909"),
        ["RowHoverBrush"] = ("#E6F4FB", "#24282E", "#141414"),
        ["RowSelectedBrush"] = ("#CFEAF7", "#1F3D57", "#06263A"),
        ["ProgressTrackBrush"] = ("#DDE6EF", "#181A1E", "#101010"),
        ["ScrollBarTrackBrush"] = ("#D9E3EE", "#1E2227", "#151515"),
        ["ScrollBarThumbBrush"] = ("#7E91A8", "#6A7482", "#565656"),
        ["ScrollBarThumbHoverBrush"] = ("#5F758E", "#8793A3", "#787878"),
        ["LinkHoverBrush"] = ("#005E92", "#9ADFFF", "#7DE7FF"),
        ["ChipTextBrush"] = ("#FFFFFF", "#0F1114", "#010101"),
        ["TrackingChipBrush"] = ("#E6F5FB", "#172838", "#061923"),
        ["TrackingChipBorderBrush"] = ("#96CBE3", "#31526A", "#06485A"),
        ["ModalOverlayBrush"] = ("#66000000", "#99000000", "#B0000000")
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        CommandBindings.Add(new CommandBinding(
            SelectHighlightedRowsCommand,
            SelectHighlightedRowsExecuted,
            CanToggleHighlightedRowsSelection));
        CommandBindings.Add(new CommandBinding(
            UnselectHighlightedRowsCommand,
            UnselectHighlightedRowsExecuted,
            CanToggleHighlightedRowsSelection));
        CommandBindings.Add(new CommandBinding(
            CopyHighlightedTrackingNumbersCommand,
            CopyHighlightedTrackingNumbersExecuted,
            CanCopyHighlightedTrackingNumbers));
        CommandBindings.Add(new CommandBinding(
            FocusSearchCommand,
            FocusSearchExecuted,
            CanFocusSearch));
        InputBindings.Add(new KeyBinding(_viewModel.NewOrderCommand, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_viewModel.SaveCurrentCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_viewModel.SaveCurrentCommand, Key.Enter, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_viewModel.CloseCurrentPanelCommand, Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(FocusSearchCommand, Key.F, ModifierKeys.Control));
        _viewModel.OrderRevealRequested += RevealOrder;
        _viewModel.Settings.PropertyChanged += SettingsPropertyChanged;
        Loaded += MainWindowLoaded;
        SizeChanged += MainWindowSizeChanged;
        StateChanged += MainWindowStateChanged;
        ApplyTheme(_viewModel.Settings.Theme);
        ApplyDensity(_viewModel.Settings.Density);
        ApplyWindowPlacement();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StoreWindowPlacement();
        _viewModel.CaptureBrowserLinkWindowPlacement();
        _viewModel.SaveNow();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.OrderRevealRequested -= RevealOrder;
        _viewModel.Settings.PropertyChanged -= SettingsPropertyChanged;
        Loaded -= MainWindowLoaded;
        SizeChanged -= MainWindowSizeChanged;
        StateChanged -= MainWindowStateChanged;
        foreach (var binding in _responsiveGridBindings)
        {
            binding.Detach();
        }

        foreach (var binding in _scrollRailBindings)
        {
            binding.Detach();
        }

        _responsiveGridBindings.Clear();
        _scrollRailBindings.Clear();
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void RevealOrder(Order order)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var grid = order.IsArchived ? ArchiveGrid : OrdersGrid;
            grid.UpdateLayout();
            grid.SelectedItem = order;
            grid.ScrollIntoView(order);
            grid.Focus();
        }));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyNativeTitleBarTheme(_viewModel.Settings.Theme);
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Theme))
        {
            ApplyTheme(_viewModel.Settings.Theme);
        }

        if (e.PropertyName == nameof(AppSettings.Density))
        {
            ApplyDensity(_viewModel.Settings.Density);
        }

        if (e.PropertyName == nameof(AppSettings.IsSidebarCollapsed))
        {
            Dispatcher.BeginInvoke((Action)RefreshScrollRailBindings, DispatcherPriority.Loaded);
        }
    }

    private void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        InitializeScrollRailBindings();
        InitializeResponsiveGridBindings();
        RefreshScrollRailBindings();
        Dispatcher.BeginInvoke(
            (Action)RefreshScrollRailBindings,
            DispatcherPriority.ContextIdle);
    }

    private void MainWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshScrollRailThumbs();
    }

    private void MainWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            _lastNonMinimizedWindowState = WindowState;
        }
    }

    private void InitializeScrollRailBindings()
    {
        if (_scrollRailBindingsInitialized)
        {
            return;
        }

        _scrollRailBindingsInitialized = true;
        AddScrollRailBinding(SidebarStatusScrollViewer, SidebarStatusScrollRail, SidebarStatusScrollColumn, SidebarStatusScrollCanvas, SidebarStatusScrollThumb);
        AddScrollRailBinding(DashboardScrollViewer, DashboardScrollRail, DashboardScrollColumn, DashboardScrollCanvas, DashboardScrollThumb);
        AddScrollRailBinding(OrdersGrid, OrdersScrollRail, OrdersScrollColumn, OrdersScrollCanvas, OrdersScrollThumb);
        AddScrollRailBinding(OrderEditorScrollViewer, OrderEditorScrollRail, OrderEditorScrollColumn, OrderEditorScrollCanvas, OrderEditorScrollThumb);
        AddScrollRailBinding(ArchiveGrid, ArchiveScrollRail, ArchiveScrollColumn, ArchiveScrollCanvas, ArchiveScrollThumb);
        AddScrollRailBinding(AccountsGrid, AccountsScrollRail, AccountsScrollColumn, AccountsScrollCanvas, AccountsScrollThumb);
        AddScrollRailBinding(AccountEditorScrollViewer, AccountEditorScrollRail, AccountEditorScrollColumn, AccountEditorScrollCanvas, AccountEditorScrollThumb);
        AddScrollRailBinding(PresetsGrid, PresetsScrollRail, PresetsScrollColumn, PresetsScrollCanvas, PresetsScrollThumb);
        AddScrollRailBinding(PresetEditorScrollViewer, PresetEditorScrollRail, PresetEditorScrollColumn, PresetEditorScrollCanvas, PresetEditorScrollThumb);
        AddScrollRailBinding(SettingsScrollViewer, SettingsScrollRail, SettingsScrollColumn, SettingsScrollCanvas, SettingsScrollThumb);
    }

    private void AddScrollRailBinding(
        FrameworkElement target,
        Border rail,
        ColumnDefinition column,
        Canvas canvas,
        Thumb thumb)
    {
        var binding = new ScrollRailBinding(this, target, rail, column, canvas, thumb);
        _scrollRailBindings.Add(binding);
        binding.Attach();
    }

    private void InitializeResponsiveGridBindings()
    {
        if (_responsiveGridBindingsInitialized)
        {
            return;
        }

        _responsiveGridBindingsInitialized = true;
        AddResponsiveGridBinding(AccountsGrid);
        AddResponsiveGridBinding(PresetsGrid);
    }

    private void AddResponsiveGridBinding(DataGrid grid)
    {
        var binding = new ResponsiveDataGridBinding(this, grid);
        _responsiveGridBindings.Add(binding);
        binding.Attach();
    }

    private void RefreshScrollRailBindings()
    {
        foreach (var binding in _scrollRailBindings)
        {
            binding.Refresh();
        }
    }

    private void RefreshScrollRailThumbs()
    {
        foreach (var binding in _scrollRailBindings)
        {
            binding.UpdateThumb();
        }
    }

    private void OrderRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: Order order } || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (_viewModel.EditOrderCommand.CanExecute(order))
        {
            _viewModel.EditOrderCommand.Execute(order);
            e.Handled = true;
        }
    }

    private void HighlightGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        QueueCommandRequery();
    }

    private void OpenRowContextMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindAncestor<DataGridRow>(button) is not { ContextMenu: { } menu } row)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Left;
        menu.HorizontalOffset = -4;
        menu.VerticalOffset = 0;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CanFocusSearch(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel.SelectedPage is AppPage.Orders or AppPage.Archive or AppPage.Accounts or AppPage.Presets;
        e.Handled = true;
    }

    private void FocusSearchExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var searchBox = _viewModel.SelectedPage switch
        {
            AppPage.Orders => FindNamedTextBox("OrdersSearchBox"),
            AppPage.Archive => FindNamedTextBox("ArchiveSearchBox"),
            AppPage.Accounts => FindNamedTextBox("AccountsSearchBox"),
            AppPage.Presets => FindNamedTextBox("ItemsSearchBox"),
            _ => null
        };

        if (searchBox is not null)
        {
            searchBox.Focus();
            searchBox.SelectAll();
        }

        e.Handled = true;
    }

    private TextBox? FindNamedTextBox(string name)
    {
        return FindName(name) as TextBox;
    }

    private void QueueCommandRequery()
    {
        if (_isCommandRequeryQueued)
        {
            return;
        }

        _isCommandRequeryQueued = true;
        Dispatcher.BeginInvoke(
            (Action)(() =>
            {
                _isCommandRequeryQueued = false;
                CommandManager.InvalidateRequerySuggested();
            }),
            DispatcherPriority.Background);
    }

    private sealed class ResponsiveDataGridBinding
    {
        private const double MinimumStarWeight = 1;
        private const double DefaultUnboundedStarWeightCap = 360;

        private readonly MainWindow _owner;
        private readonly DataGrid _grid;
        private bool _isInitialized;
        private bool _isInitializeQueued;
        private bool _isApplyQueued;
        private bool _isDetached;

        public ResponsiveDataGridBinding(MainWindow owner, DataGrid grid)
        {
            _owner = owner;
            _grid = grid;
        }

        public void Attach()
        {
            _isDetached = false;
            _grid.Loaded += GridLoaded;
            _grid.IsVisibleChanged += GridIsVisibleChanged;
            _grid.SizeChanged += GridSizeChanged;
            QueueInitialize(DispatcherPriority.Loaded);
        }

        public void Detach()
        {
            _isDetached = true;
            _grid.Loaded -= GridLoaded;
            _grid.IsVisibleChanged -= GridIsVisibleChanged;
            _grid.SizeChanged -= GridSizeChanged;
        }

        private void QueueInitialize(DispatcherPriority priority)
        {
            if (_isDetached || _isInitialized || _isInitializeQueued || _isApplyQueued || !CanInitialize())
            {
                return;
            }

            _isInitializeQueued = true;
            _owner.Dispatcher.BeginInvoke(
                (Action)(() =>
                {
                    _isInitializeQueued = false;
                    InitializeFromAutoWidths();
                }),
                priority);
        }

        private void InitializeFromAutoWidths()
        {
            if (_isDetached || _isInitialized || !CanInitialize())
            {
                return;
            }

            foreach (var column in _grid.Columns.Where(column => column.Visibility == Visibility.Visible))
            {
                column.Width = DataGridLength.Auto;
            }

            _grid.UpdateLayout();
            QueueApplyResponsiveWidths();
        }

        private void QueueApplyResponsiveWidths()
        {
            if (_isApplyQueued)
            {
                return;
            }

            _isApplyQueued = true;
            _owner.Dispatcher.BeginInvoke(
                (Action)(() =>
                {
                    _isApplyQueued = false;
                    ApplyResponsiveWidths();
                }),
                DispatcherPriority.ContextIdle);
        }

        private void ApplyResponsiveWidths()
        {
            if (_isDetached || _isInitialized || !CanInitialize())
            {
                return;
            }

            foreach (var column in _grid.Columns.Where(column => column.Visibility == Visibility.Visible))
            {
                var weight = GetResponsiveStarWeight(column);
                column.Width = new DataGridLength(weight, DataGridLengthUnitType.Star);
            }

            _grid.ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star);
            _isInitialized = true;
        }

        private bool CanInitialize()
        {
            return _grid.IsLoaded &&
                   _grid.IsVisible &&
                   _grid.ActualWidth > 0 &&
                   _grid.Columns.Count > 0;
        }

        private static double GetResponsiveStarWeight(DataGridColumn column)
        {
            var measuredWidth = column.ActualWidth;
            var minimumWidth = Math.Max(MinimumStarWeight, column.MinWidth);
            var maximumWeight = double.IsNaN(column.MaxWidth) || double.IsInfinity(column.MaxWidth)
                ? DefaultUnboundedStarWeightCap
                : Math.Max(minimumWidth, column.MaxWidth);

            if (double.IsNaN(measuredWidth) || measuredWidth <= 0)
            {
                measuredWidth = column.Width.Value > 0
                    ? column.Width.Value
                    : minimumWidth;
            }

            return Math.Clamp(measuredWidth, minimumWidth, maximumWeight);
        }

        private void GridLoaded(object sender, RoutedEventArgs e)
        {
            QueueInitialize(DispatcherPriority.Loaded);
        }

        private void GridIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            QueueInitialize(DispatcherPriority.Loaded);
        }

        private void GridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueInitialize(DispatcherPriority.Loaded);
        }
    }

    private sealed class ScrollRailBinding
    {
        private const double RailWidth = 18;

        private readonly MainWindow _owner;
        private readonly FrameworkElement _target;
        private readonly Border _rail;
        private readonly ColumnDefinition _column;
        private readonly Canvas _canvas;
        private readonly Thumb _thumb;
        private ScrollViewer? _scrollViewer;
        private double _dragStartOffset;
        private double _dragDeltaY;
        private bool _isRefreshQueued;
        private bool _isDetached;

        public ScrollRailBinding(
            MainWindow owner,
            FrameworkElement target,
            Border rail,
            ColumnDefinition column,
            Canvas canvas,
            Thumb thumb)
        {
            _owner = owner;
            _target = target;
            _rail = rail;
            _column = column;
            _canvas = canvas;
            _thumb = thumb;
        }

        public void Attach()
        {
            _isDetached = false;
            HideNativeVerticalScrollBar();
            _target.Loaded += TargetLoaded;
            _target.IsVisibleChanged += TargetIsVisibleChanged;
            _target.SizeChanged += TargetSizeChanged;
            _canvas.SizeChanged += CanvasSizeChanged;
            _rail.PreviewMouseLeftButtonDown += RailPreviewMouseLeftButtonDown;
            _thumb.DragStarted += ThumbDragStarted;
            _thumb.DragDelta += ThumbDragDelta;
            _thumb.DragCompleted += ThumbDragCompleted;
            Refresh();
        }

        public void Detach()
        {
            _isDetached = true;
            _target.Loaded -= TargetLoaded;
            _target.IsVisibleChanged -= TargetIsVisibleChanged;
            _target.SizeChanged -= TargetSizeChanged;
            _canvas.SizeChanged -= CanvasSizeChanged;
            _rail.PreviewMouseLeftButtonDown -= RailPreviewMouseLeftButtonDown;
            _thumb.DragStarted -= ThumbDragStarted;
            _thumb.DragDelta -= ThumbDragDelta;
            _thumb.DragCompleted -= ThumbDragCompleted;

            if (_scrollViewer is not null)
            {
                _scrollViewer.ScrollChanged -= ScrollViewerScrollChanged;
                _scrollViewer.SizeChanged -= ScrollViewerSizeChanged;
                _scrollViewer = null;
            }
        }

        public void Refresh()
        {
            if (_isDetached)
            {
                return;
            }

            if (_target is not ScrollViewer)
            {
                _target.ApplyTemplate();
            }

            var scrollViewer = _target as ScrollViewer ?? FindVisualChildren<ScrollViewer>(_target).FirstOrDefault();
            if (scrollViewer is null)
            {
                SetRailVisible(false);
                if (_target.IsLoaded && _target.IsVisible)
                {
                    QueueRefresh(DispatcherPriority.Loaded);
                }

                return;
            }

            if (!ReferenceEquals(_scrollViewer, scrollViewer))
            {
                if (_scrollViewer is not null)
                {
                    _scrollViewer.ScrollChanged -= ScrollViewerScrollChanged;
                    _scrollViewer.SizeChanged -= ScrollViewerSizeChanged;
                }

                _scrollViewer = scrollViewer;
                _scrollViewer.ScrollChanged += ScrollViewerScrollChanged;
                _scrollViewer.SizeChanged += ScrollViewerSizeChanged;
                _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            }

            UpdateThumb();
        }

        public void UpdateThumb()
        {
            if (_scrollViewer is null)
            {
                SetRailVisible(false);
                return;
            }

            var scrollableHeight = _scrollViewer.ScrollableHeight;
            var shouldShow =
                _target.IsVisible &&
                _target.ActualHeight > 0 &&
                _target.ActualWidth > 0 &&
                scrollableHeight > 0;

            if (!shouldShow)
            {
                SetRailVisible(false);
                return;
            }

            var trackLength = _canvas.ActualHeight;
            if (trackLength <= 0)
            {
                SetRailVisible(true);
                QueueRefresh(DispatcherPriority.Loaded);
                return;
            }

            SetRailVisible(true);
            var thumbLength = ModernScrollBarAssist.GetThumbLength(trackLength, 0, scrollableHeight, _scrollViewer.ViewportHeight);
            thumbLength = Math.Min(thumbLength, trackLength);
            _thumb.Height = thumbLength;

            var availableTrackLength = Math.Max(0, trackLength - thumbLength);
            var thumbTop = availableTrackLength <= 0
                ? 0
                : availableTrackLength * Math.Min(_scrollViewer.VerticalOffset, scrollableHeight) / scrollableHeight;

            Canvas.SetTop(_thumb, thumbTop);
        }

        private void HideNativeVerticalScrollBar()
        {
            if (_target is ScrollViewer scrollViewer)
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                return;
            }

            ScrollViewer.SetVerticalScrollBarVisibility(_target, ScrollBarVisibility.Hidden);
        }

        private void QueueRefresh(DispatcherPriority priority)
        {
            if (_isRefreshQueued)
            {
                return;
            }

            _isRefreshQueued = true;
            _owner.Dispatcher.BeginInvoke(
                (Action)(() =>
                {
                    _isRefreshQueued = false;
                    if (_isDetached)
                    {
                        return;
                    }

                    Refresh();
                }),
                priority);
        }

        private void SetRailVisible(bool isVisible)
        {
            _rail.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            _column.Width = isVisible ? new GridLength(RailWidth) : new GridLength(0);
        }

        private void ScrollToOffset(double targetOffset)
        {
            if (_scrollViewer is null)
            {
                return;
            }

            var offset = Math.Clamp(targetOffset, 0, _scrollViewer.ScrollableHeight);
            _scrollViewer.ScrollToVerticalOffset(offset);
        }

        private void TargetLoaded(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void TargetIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            QueueRefresh(DispatcherPriority.Loaded);
        }

        private void TargetSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateThumb();
        }

        private void CanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateThumb();
        }

        private void ScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateThumb();
        }

        private void ScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateThumb();
        }

        private void RailPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_scrollViewer is null || _scrollViewer.ScrollableHeight <= 0 || _thumb.IsMouseOver)
            {
                return;
            }

            var trackLength = _canvas.ActualHeight;
            var thumbLength = _thumb.ActualHeight;
            var availableTrackLength = trackLength - thumbLength;
            if (trackLength <= 0 || availableTrackLength <= 0)
            {
                return;
            }

            var pointerY = e.GetPosition(_canvas).Y;
            var targetOffset = (pointerY - thumbLength / 2) / availableTrackLength * _scrollViewer.ScrollableHeight;
            ScrollToOffset(targetOffset);
            e.Handled = true;
        }

        private void ThumbDragStarted(object sender, DragStartedEventArgs e)
        {
            if (_scrollViewer is null || _scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            _dragStartOffset = _scrollViewer.VerticalOffset;
            _dragDeltaY = 0;
            e.Handled = true;
        }

        private void ThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_scrollViewer is null || _scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            var trackLength = _canvas.ActualHeight;
            var thumbLength = _thumb.ActualHeight;
            var availableTrackLength = trackLength - thumbLength;
            if (availableTrackLength <= 0)
            {
                return;
            }

            _dragDeltaY += e.VerticalChange;
            var targetOffset = _dragStartOffset + _dragDeltaY / availableTrackLength * _scrollViewer.ScrollableHeight;
            ScrollToOffset(targetOffset);
            e.Handled = true;
        }

        private void ThumbDragCompleted(object sender, DragCompletedEventArgs e)
        {
            UpdateThumb();
            e.Handled = true;
        }
    }

    private void CanToggleHighlightedRowsSelection(object sender, CanExecuteRoutedEventArgs e)
    {
        var grid = GetCommandGrid(e);
        var fallback = GetCommandItem(e);
        e.CanExecute =
            grid?.SelectedItems.Cast<object>().Any(IsBulkSelectableItem) == true ||
            fallback is not null && IsBulkSelectableItem(fallback);
        e.Handled = true;
    }

    private void SelectHighlightedRowsExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SetHighlightedRowsSelection(e, true);
    }

    private void UnselectHighlightedRowsExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SetHighlightedRowsSelection(e, false);
    }

    private void CanCopyHighlightedTrackingNumbers(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = GetCommandOrders(e).Count > 0;
        e.Handled = true;
    }

    private void CopyHighlightedTrackingNumbersExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var orders = GetCommandOrders(e);
        if (orders.Count == 0)
        {
            _viewModel.LastActionMessage = "Highlight or select orders first, then copy tracking numbers.";
            e.Handled = true;
            return;
        }

        _viewModel.CopyTrackingNumbers(orders);
        e.Handled = true;
    }

    private void SetHighlightedRowsSelection(ExecutedRoutedEventArgs e, bool isSelected)
    {
        var grid = GetCommandGrid(e);
        var fallback = GetCommandItem(e);
        var highlightedItems = grid is null
            ? new List<object>()
            : grid.SelectedItems
                .Cast<object>()
                .Where(IsBulkSelectableItem)
                .ToList();

        if (highlightedItems.Count == 0 && fallback is not null && IsBulkSelectableItem(fallback))
        {
            highlightedItems.Add(fallback);
        }

        if (highlightedItems.Count == 0)
        {
            var action = isSelected ? "select" : "unselect";
            _viewModel.LastActionMessage = $"Highlight rows first, then {action} them.";
            e.Handled = true;
            return;
        }

        var changed = 0;
        changed = _viewModel.SetBulkSelection(highlightedItems, isSelected);

        var rowWord = highlightedItems.Count == 1 ? "row" : "rows";
        if (changed == 0)
        {
            var alreadyState = isSelected ? "already selected" : "already unselected";
            _viewModel.LastActionMessage = $"{highlightedItems.Count} highlighted {rowWord} {alreadyState}.";
        }
        else
        {
            var action = isSelected ? "Selected" : "Unselected";
            _viewModel.LastActionMessage = $"{action} {changed} highlighted {rowWord}.";
        }

        e.Handled = true;
    }

    private static List<Order> GetCommandOrders(RoutedEventArgs e)
    {
        var grid = GetCommandGrid(e);
        var fallback = GetCommandItem(e) as Order;
        var orders = new List<Order>();

        if (grid is not null)
        {
            orders.AddRange(grid.SelectedItems.Cast<object>().OfType<Order>());
            orders.AddRange(grid.Items.Cast<object>().OfType<Order>().Where(order => order.IsSelected));
        }

        if (orders.Count == 0 && fallback is not null)
        {
            orders.Add(fallback);
        }

        return orders
            .Distinct()
            .ToList();
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or ComboBox or TextBoxBase or DatePicker)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsBulkSelectableItem(object item)
    {
        return item is Order or AccountPreset or ItemPreset;
    }

    private static DataGrid? GetCommandGrid(RoutedEventArgs e)
    {
        if (e.Source is DataGrid sourceGrid)
        {
            return sourceGrid;
        }

        if (e.OriginalSource is DataGrid originalGrid)
        {
            return originalGrid;
        }

        if (e.Source is DependencyObject sourceDependency)
        {
            var sourceParentGrid = FindVisualParent<DataGrid>(sourceDependency);
            if (sourceParentGrid is not null)
            {
                return sourceParentGrid;
            }
        }

        return FindVisualParent<DataGrid>(e.OriginalSource as DependencyObject);
    }

    private static object? GetCommandItem(RoutedEventArgs e)
    {
        if (e.Source is FrameworkElement { DataContext: { } sourceContext } && IsBulkSelectableItem(sourceContext))
        {
            return sourceContext;
        }

        if (e.OriginalSource is FrameworkElement { DataContext: { } originalContext } && IsBulkSelectableItem(originalContext))
        {
            return originalContext;
        }

        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject source)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(source);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(source);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ApplyDensity(UiDensity density)
    {
        var compact = density == UiDensity.Compact;
        Resources["GridHeaderPadding"] = compact ? new Thickness(8, 5, 8, 5) : new Thickness(10, 7, 10, 7);
        Resources["GridHeaderMinHeight"] = compact ? 32d : 38d;
        Resources["GridCellPadding"] = compact ? new Thickness(8, 5, 8, 5) : new Thickness(10, 7, 10, 7);
        Resources["OrderRowMinHeight"] = compact ? 44d : 52d;
        Resources["PresetRowMinHeight"] = compact ? 40d : 48d;
        Resources["GridTextFontSize"] = compact ? 11.5d : 12.5d;
        Resources["GridActionMinHeight"] = compact ? 24d : 26d;
        Resources["GridActionFontSize"] = compact ? 11d : 11.5d;
    }

    private void ApplyTheme(AppTheme theme)
    {
        foreach (var (key, colors) in ThemeBrushes)
        {
            var color = theme switch
            {
                AppTheme.Light => colors.Light,
                AppTheme.OLED => colors.Oled,
                _ => colors.Dark
            };
            SetBrushColor(key, color);
        }

        Background = (Brush)Resources["AppBackgroundBrush"];
        ApplyNativeTitleBarTheme(theme);
    }

    private void ApplyNativeTitleBarTheme(AppTheme theme)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var isDarkTitleBar = theme != AppTheme.Light ? 1 : 0;
        _ = DwmSetWindowAttribute(
            handle,
            DwmAttributeUseImmersiveDarkMode,
            ref isDarkTitleBar,
            Marshal.SizeOf<int>());

        var captionColor = theme switch
        {
            AppTheme.Light => ToColorRef("#F3F6FA"),
            AppTheme.OLED => ToColorRef("#000000"),
            _ => ToColorRef("#101113")
        };
        var textColor = theme switch
        {
            AppTheme.Light => ToColorRef("#111827"),
            AppTheme.OLED => ToColorRef("#F4F4F4"),
            _ => ToColorRef("#F1F3F5")
        };
        var borderColor = theme switch
        {
            AppTheme.Light => ToColorRef("#CBD7E3"),
            AppTheme.OLED => ToColorRef("#303030"),
            _ => ToColorRef("#343A42")
        };

        _ = DwmSetWindowAttribute(handle, DwmAttributeCaptionColor, ref captionColor, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmAttributeTextColor, ref textColor, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmAttributeBorderColor, ref borderColor, Marshal.SizeOf<int>());
    }

    private static int ToColorRef(string color)
    {
        if (ColorConverter.ConvertFromString(color) is not Color parsed)
        {
            return 0;
        }

        return parsed.R | (parsed.G << 8) | (parsed.B << 16);
    }

    private void SetBrushColor(string resourceKey, string color)
    {
        if (ColorConverter.ConvertFromString(color) is Color parsed)
        {
            Resources[resourceKey] = new SolidColorBrush(parsed);
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    private void ApplyWindowPlacement()
    {
        var settings = _viewModel.Settings;
        var hasStoredSize = IsUsableLength(settings.WindowWidth) && IsUsableLength(settings.WindowHeight);
        var workAreas = GetMonitorWorkAreas();
        var primaryWorkArea = SystemParameters.WorkArea;
        var width = hasStoredSize ? Math.Max(MinWidth, settings.WindowWidth) : Width;
        var height = hasStoredSize ? Math.Max(MinHeight, settings.WindowHeight) : Height;

        if (hasStoredSize && !primaryWorkArea.IsEmpty)
        {
            Width = Math.Max(MinWidth, Math.Min(width, primaryWorkArea.Width));
            Height = Math.Max(MinHeight, Math.Min(height, primaryWorkArea.Height));
        }

        if (settings.WindowLeft is { } left &&
            settings.WindowTop is { } top &&
            IsFinite(left) &&
            IsFinite(top))
        {
            var desiredBounds = new Rect(left, top, width, height);
            var targetWorkArea = SelectTargetWorkArea(desiredBounds, workAreas);
            var restoredBounds = ClampToWorkArea(desiredBounds, targetWorkArea, MinWidth, MinHeight);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = restoredBounds.Width;
            Height = restoredBounds.Height;
            Left = restoredBounds.Left;
            Top = restoredBounds.Top;
        }

        if (settings.IsWindowMaximized)
        {
            _lastNonMinimizedWindowState = WindowState.Maximized;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _lastNonMinimizedWindowState = WindowState.Normal;
        }
    }

    private void StoreWindowPlacement()
    {
        var settings = _viewModel.Settings;
        var intendedState = GetIntendedWindowState(WindowState, _lastNonMinimizedWindowState);
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        settings.IsWindowMaximized = intendedState == WindowState.Maximized;

        if (!IsUsableLength(bounds.Width) || !IsUsableLength(bounds.Height))
        {
            return;
        }

        settings.WindowWidth = Math.Max(MinWidth, bounds.Width);
        settings.WindowHeight = Math.Max(MinHeight, bounds.Height);
        settings.WindowLeft = IsFinite(bounds.Left) ? bounds.Left : null;
        settings.WindowTop = IsFinite(bounds.Top) ? bounds.Top : null;
    }

    private static WindowState GetIntendedWindowState(WindowState currentState, WindowState lastNonMinimizedState)
    {
        return currentState == WindowState.Minimized ? lastNonMinimizedState : currentState;
    }

    private static Rect ClampToWorkArea(Rect bounds, Rect workArea, double minWidth, double minHeight)
    {
        var width = Math.Max(minWidth, Math.Min(bounds.Width, workArea.Width));
        var height = Math.Max(minHeight, Math.Min(bounds.Height, workArea.Height));
        var left = width <= workArea.Width
            ? Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width)
            : workArea.Left;
        var top = height <= workArea.Height
            ? Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height)
            : workArea.Top;
        return new Rect(left, top, width, height);
    }

    private static Rect SelectTargetWorkArea(Rect bounds, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0)
        {
            return SystemParameters.WorkArea;
        }

        return workAreas
            .OrderByDescending(workArea => GetIntersectionArea(bounds, workArea))
            .ThenBy(workArea => GetDistanceSquared(bounds, workArea))
            .First();
    }

    private static double GetIntersectionArea(Rect first, Rect second)
    {
        var width = Math.Max(0d, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var height = Math.Max(0d, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return width * height;
    }

    private static double GetDistanceSquared(Rect bounds, Rect workArea)
    {
        var centerX = bounds.Left + bounds.Width / 2d;
        var centerY = bounds.Top + bounds.Height / 2d;
        var nearestX = Math.Clamp(centerX, workArea.Left, workArea.Right);
        var nearestY = Math.Clamp(centerY, workArea.Top, workArea.Bottom);
        var deltaX = centerX - nearestX;
        var deltaY = centerY - nearestY;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private static IReadOnlyList<Rect> GetMonitorWorkAreas()
    {
        var workAreas = new List<Rect>();
        var scale = GetSystemDpiScale();
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                workAreas.Add(new Rect(
                    info.WorkArea.Left / scale,
                    info.WorkArea.Top / scale,
                    (info.WorkArea.Right - info.WorkArea.Left) / scale,
                    (info.WorkArea.Bottom - info.WorkArea.Top) / scale));
            }

            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || workAreas.Count == 0)
        {
            workAreas.Add(SystemParameters.WorkArea);
        }

        return workAreas;
    }

    private static double GetSystemDpiScale()
    {
        try
        {
            return Math.Max(1d, GetDpiForSystem() / 96d);
        }
        catch (EntryPointNotFoundException)
        {
            return 1d;
        }
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static bool IsUsableLength(double value)
    {
        return IsFinite(value) && value > 0;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
