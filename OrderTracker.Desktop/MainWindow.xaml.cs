using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.ViewModels;

namespace OrderTracker.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    public static readonly RoutedUICommand SelectHighlightedRowsCommand = new(
        "Select",
        nameof(SelectHighlightedRowsCommand),
        typeof(MainWindow));

    public static readonly RoutedUICommand UnselectHighlightedRowsCommand = new(
        "Unselect",
        nameof(UnselectHighlightedRowsCommand),
        typeof(MainWindow));

    private static readonly Dictionary<string, (string Light, string Dark, string Oled)> ThemeBrushes = new()
    {
        ["AppBackgroundBrush"] = ("#F3F6FA", "#0E1117", "#000000"),
        ["SidebarBrush"] = ("#FFFFFF", "#131923", "#030303"),
        ["SurfaceBrush"] = ("#FFFFFF", "#171D28", "#050505"),
        ["SurfaceAltBrush"] = ("#EEF3F8", "#1D2633", "#0B0B0B"),
        ["ControlBrush"] = ("#F7FAFD", "#111722", "#000000"),
        ["ControlHoverBrush"] = ("#E8F1F8", "#223044", "#101010"),
        ["BorderBrush"] = ("#CBD7E3", "#2B3647", "#262626"),
        ["TextBrush"] = ("#111827", "#EEF3F8", "#F5F7FA"),
        ["MutedTextBrush"] = ("#516174", "#97A4B5", "#9AA4AF"),
        ["AccentBrush"] = ("#0078B8", "#5CC8FF", "#00E5FF"),
        ["AccentDarkBrush"] = ("#D8F0FB", "#1E5870", "#004D57"),
        ["SuccessBrush"] = ("#168765", "#2F9E7E", "#33D69F"),
        ["DangerBrush"] = ("#C93D4B", "#E05D5D", "#FF6B6B"),
        ["DangerSurfaceBrush"] = ("#FBE4E7", "#53242A", "#2A0E12"),
        ["DataGridHeaderBrush"] = ("#EAF0F6", "#101722", "#000000"),
        ["DataGridAlternateBrush"] = ("#F7FAFD", "#151B25", "#050505"),
        ["RowHoverBrush"] = ("#E6F4FB", "#1B2635", "#0D181A"),
        ["RowSelectedBrush"] = ("#CFEAF7", "#233A50", "#10292F"),
        ["ProgressTrackBrush"] = ("#DDE6EF", "#101722", "#101010"),
        ["ScrollBarTrackBrush"] = ("#E8EEF5", "#101722", "#000000"),
        ["ScrollBarThumbBrush"] = ("#A7B6C7", "#405067", "#303030"),
        ["ScrollBarThumbHoverBrush"] = ("#7F91A8", "#5D708C", "#505050"),
        ["LinkHoverBrush"] = ("#005E92", "#A7E4FF", "#86F2FF"),
        ["ChipTextBrush"] = ("#FFFFFF", "#0E1117", "#010101"),
        ["TrackingChipBrush"] = ("#E6F5FB", "#102332", "#031B22"),
        ["TrackingChipBorderBrush"] = ("#96CBE3", "#244A62", "#064A59"),
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
        _viewModel.Settings.PropertyChanged += SettingsPropertyChanged;
        ApplyTheme(_viewModel.Settings.Theme);
        ApplyWindowPlacement();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StoreWindowPlacement();
        _viewModel.SaveNow();
        base.OnClosing(e);
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Theme))
        {
            ApplyTheme(_viewModel.Settings.Theme);
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

    private void SelectableGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommandManager.InvalidateRequerySuggested();
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

    private void SetHighlightedRowsSelection(ExecutedRoutedEventArgs e, bool isSelected)
    {
        var grid = GetCommandGrid(e);
        if (grid is null)
        {
            return;
        }

        var highlightedItems = grid.SelectedItems
            .Cast<object>()
            .Where(IsBulkSelectableItem)
            .ToList();

        var fallback = GetCommandItem(e);
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
        foreach (var item in highlightedItems)
        {
            changed += SetBulkSelected(item, isSelected) ? 1 : 0;
        }

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

    private static bool SetBulkSelected(object item, bool isSelected)
    {
        switch (item)
        {
            case Order order when order.IsSelected != isSelected:
                order.IsSelected = isSelected;
                return true;
            case AccountPreset accountPreset when accountPreset.IsSelected != isSelected:
                accountPreset.IsSelected = isSelected;
                return true;
            case ItemPreset itemPreset when itemPreset.IsSelected != isSelected:
                itemPreset.IsSelected = isSelected;
                return true;
            default:
                return false;
        }
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
    }

    private void SetBrushColor(string resourceKey, string color)
    {
        if (ColorConverter.ConvertFromString(color) is Color parsed)
        {
            Resources[resourceKey] = new SolidColorBrush(parsed);
        }
    }

    private void ApplyWindowPlacement()
    {
        var settings = _viewModel.Settings;
        var hasStoredSize = IsUsableLength(settings.WindowWidth) && IsUsableLength(settings.WindowHeight);

        if (hasStoredSize)
        {
            Width = Math.Max(MinWidth, settings.WindowWidth);
            Height = Math.Max(MinHeight, settings.WindowHeight);
        }

        if (settings.WindowLeft is { } left &&
            settings.WindowTop is { } top &&
            IsVisiblePlacement(left, top, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (settings.IsWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void StoreWindowPlacement()
    {
        var settings = _viewModel.Settings;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;

        if (!IsUsableLength(bounds.Width) || !IsUsableLength(bounds.Height))
        {
            return;
        }

        settings.WindowWidth = Math.Max(MinWidth, bounds.Width);
        settings.WindowHeight = Math.Max(MinHeight, bounds.Height);
        settings.WindowLeft = IsFinite(bounds.Left) ? bounds.Left : null;
        settings.WindowTop = IsFinite(bounds.Top) ? bounds.Top : null;
        settings.IsWindowMaximized = WindowState == WindowState.Maximized;
    }

    private static bool IsVisiblePlacement(double left, double top, double width, double height)
    {
        var right = left + width;
        var bottom = top + height;
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

        return left < screenRight - 80 &&
               top < screenBottom - 80 &&
               right > screenLeft + 80 &&
               bottom > screenTop + 80;
    }

    private static bool IsUsableLength(double value)
    {
        return IsFinite(value) && value > 0;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
