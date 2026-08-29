using System.Collections.Concurrent;
using System.Windows.Media;

namespace OrderTracker.Desktop.Utilities;

public static class BrushCache
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Brushes = new();

    public static SolidColorBrush Get(string hex)
    {
        return Brushes.GetOrAdd(hex, static key =>
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(key)!;
            brush.Freeze();
            return brush;
        });
    }
}
