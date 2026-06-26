using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Services;

namespace OrderTracker.Desktop.Converters;

public sealed class MerchantFaviconConverter : IMultiValueConverter
{
    private static readonly Dictionary<string, ImageSource> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var merchant = values.FirstOrDefault() is MerchantKind merchantValue
            ? merchantValue
            : MerchantKind.Unknown;
        var imageSource = LoadCachedIcon(merchant);
        var mode = parameter?.ToString();

        if (string.Equals(mode, "IconVisibility", StringComparison.OrdinalIgnoreCase))
        {
            return imageSource is null ? Visibility.Collapsed : Visibility.Visible;
        }

        if (string.Equals(mode, "FallbackVisibility", StringComparison.OrdinalIgnoreCase))
        {
            return imageSource is null ? Visibility.Visible : Visibility.Collapsed;
        }

        return imageSource;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static ImageSource? LoadCachedIcon(MerchantKind merchant)
    {
        var path = MerchantFaviconService.FindCachedIconPath(merchant);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fileInfo = new FileInfo(path);
        var cacheKey = $"{path}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        if (ImageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderByDescending(candidate => candidate.PixelWidth * candidate.PixelHeight)
                .FirstOrDefault();
            if (frame is null)
            {
                return null;
            }

            frame.Freeze();
            ImageCache[cacheKey] = frame;
            return frame;
        }
        catch
        {
            return null;
        }
    }
}
