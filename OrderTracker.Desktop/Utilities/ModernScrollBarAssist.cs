using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace OrderTracker.Desktop.Utilities;

public static class ModernScrollBarAssist
{
    public const double MinimumThumbRatio = 0.34;
    public const double MinimumThumbLength = 56;
    public const double MaximumThumbRatio = 0.82;

    public static readonly DependencyProperty UseLongThumbProperty = DependencyProperty.RegisterAttached(
        "UseLongThumb",
        typeof(bool),
        typeof(ModernScrollBarAssist),
        new PropertyMetadata(false, UseLongThumbChanged));

    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked",
        typeof(bool),
        typeof(ModernScrollBarAssist),
        new PropertyMetadata(false));

    private static readonly DependencyPropertyDescriptor? MinimumDescriptor =
        DependencyPropertyDescriptor.FromProperty(RangeBase.MinimumProperty, typeof(ScrollBar));

    private static readonly DependencyPropertyDescriptor? MaximumDescriptor =
        DependencyPropertyDescriptor.FromProperty(RangeBase.MaximumProperty, typeof(ScrollBar));

    private static readonly DependencyPropertyDescriptor? ViewportDescriptor =
        DependencyPropertyDescriptor.FromProperty(ScrollBar.ViewportSizeProperty, typeof(ScrollBar));

    public static void SetUseLongThumb(DependencyObject element, bool value)
    {
        element.SetValue(UseLongThumbProperty, value);
    }

    public static bool GetUseLongThumb(DependencyObject element)
    {
        return (bool)element.GetValue(UseLongThumbProperty);
    }

    public static double GetThumbLength(double trackLength, double minimum, double maximum, double viewportSize)
    {
        if (double.IsNaN(trackLength) || double.IsInfinity(trackLength) || trackLength <= 0)
        {
            return 0;
        }

        var minimumLength = Math.Min(MinimumThumbLength, trackLength * MaximumThumbRatio);
        var proportionalLength = trackLength * MinimumThumbRatio;
        var desiredLength = Math.Min(Math.Max(minimumLength, proportionalLength), trackLength * MaximumThumbRatio);

        if (double.IsNaN(minimum) ||
            double.IsNaN(maximum) ||
            double.IsNaN(viewportSize) ||
            double.IsInfinity(minimum) ||
            double.IsInfinity(maximum) ||
            double.IsInfinity(viewportSize))
        {
            return desiredLength;
        }

        var range = maximum - minimum;
        if (range <= 0 || viewportSize <= 0)
        {
            return desiredLength;
        }

        var naturalLength = trackLength * viewportSize / (range + viewportSize);
        return Math.Min(Math.Max(desiredLength, naturalLength), trackLength * MaximumThumbRatio);
    }

    private static void UseLongThumbChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        if (source is not ScrollBar scrollBar)
        {
            return;
        }

        if (e.NewValue is true)
        {
            HookScrollBar(scrollBar);
        }
        else
        {
            UnhookScrollBar(scrollBar);
        }
    }

    private static void HookScrollBar(ScrollBar scrollBar)
    {
        if ((bool)scrollBar.GetValue(IsHookedProperty))
        {
            AttachMetricHandlers(scrollBar);
            QueueThumbUpdate(scrollBar);
            return;
        }

        scrollBar.Loaded += ScrollBarLoaded;
        scrollBar.Unloaded += ScrollBarUnloaded;
        scrollBar.SizeChanged += ScrollBarSizeChanged;
        scrollBar.SetValue(IsHookedProperty, true);

        AttachMetricHandlers(scrollBar);
        QueueThumbUpdate(scrollBar);
    }

    private static void UnhookScrollBar(ScrollBar scrollBar)
    {
        if (!(bool)scrollBar.GetValue(IsHookedProperty))
        {
            return;
        }

        scrollBar.Loaded -= ScrollBarLoaded;
        scrollBar.Unloaded -= ScrollBarUnloaded;
        scrollBar.SizeChanged -= ScrollBarSizeChanged;
        DetachMetricHandlers(scrollBar);
        scrollBar.SetValue(IsHookedProperty, false);
    }

    private static void ScrollBarLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollBar scrollBar)
        {
            AttachMetricHandlers(scrollBar);
            QueueThumbUpdate(scrollBar);
        }
    }

    private static void ScrollBarUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollBar scrollBar)
        {
            DetachMetricHandlers(scrollBar);
        }
    }

    private static void ScrollBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollBar scrollBar)
        {
            UpdateThumbSize(scrollBar);
        }
    }

    private static void ScrollBarMetricChanged(object? sender, EventArgs e)
    {
        if (sender is ScrollBar scrollBar)
        {
            UpdateThumbSize(scrollBar);
        }
    }

    private static void AttachMetricHandlers(ScrollBar scrollBar)
    {
        DetachMetricHandlers(scrollBar);
        MinimumDescriptor?.AddValueChanged(scrollBar, ScrollBarMetricChanged);
        MaximumDescriptor?.AddValueChanged(scrollBar, ScrollBarMetricChanged);
        ViewportDescriptor?.AddValueChanged(scrollBar, ScrollBarMetricChanged);
    }

    private static void DetachMetricHandlers(ScrollBar scrollBar)
    {
        MinimumDescriptor?.RemoveValueChanged(scrollBar, ScrollBarMetricChanged);
        MaximumDescriptor?.RemoveValueChanged(scrollBar, ScrollBarMetricChanged);
        ViewportDescriptor?.RemoveValueChanged(scrollBar, ScrollBarMetricChanged);
    }

    private static void QueueThumbUpdate(ScrollBar scrollBar)
    {
        scrollBar.Dispatcher.BeginInvoke(
            (Action)(() => UpdateThumbSize(scrollBar)),
            DispatcherPriority.Loaded);
    }

    private static void UpdateThumbSize(ScrollBar scrollBar)
    {
        if (scrollBar.Template?.FindName("PART_Track", scrollBar) is not Track track ||
            track.Thumb is null)
        {
            return;
        }

        if (scrollBar.Orientation == Orientation.Vertical)
        {
            var thumbLength = GetThumbLength(
                track.ActualHeight,
                scrollBar.Minimum,
                scrollBar.Maximum,
                scrollBar.ViewportSize);

            track.Thumb.MinHeight = thumbLength;
            track.Thumb.Height = thumbLength;
            track.Thumb.MinWidth = 0;
            track.Thumb.Width = double.NaN;
        }
        else
        {
            var thumbLength = GetThumbLength(
                track.ActualWidth,
                scrollBar.Minimum,
                scrollBar.Maximum,
                scrollBar.ViewportSize);

            track.Thumb.MinWidth = thumbLength;
            track.Thumb.Width = thumbLength;
            track.Thumb.MinHeight = 0;
            track.Thumb.Height = double.NaN;
        }

        track.InvalidateMeasure();
        track.InvalidateArrange();
    }
}
