using System.IO;
using System.Net.Http;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class MerchantFaviconService
{
    private static readonly string[] SupportedExtensions = { ".png", ".ico", ".jpg", ".jpeg", ".gif", ".bmp" };
    private static readonly IReadOnlyDictionary<MerchantKind, string> MerchantDomains =
        new Dictionary<MerchantKind, string>
        {
            [MerchantKind.Amazon] = "amazon.com",
            [MerchantKind.Walmart] = "walmart.com",
            [MerchantKind.Target] = "target.com",
            [MerchantKind.BestBuy] = "bestbuy.com",
            [MerchantKind.eBay] = "ebay.com"
        };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public MerchantFaviconService()
    {
        CacheFolder = AppPaths.IconCacheFolder;
    }

    public string CacheFolder { get; }

    public int CachedIconCount => AppSettings.ListedMerchants.Count(merchant => FindCachedIconPath(merchant) is not null);

    public static string? FindCachedIconPath(MerchantKind merchant)
    {
        var folder = AppPaths.IconCacheFolder;
        var stem = GetCacheStem(merchant);
        if (string.IsNullOrWhiteSpace(stem) || !Directory.Exists(folder))
        {
            return null;
        }

        return SupportedExtensions
            .Select(extension => Path.Combine(folder, $"{stem}{extension}"))
            .FirstOrDefault(File.Exists);
    }

    public async Task<bool> EnsureIconAsync(MerchantKind merchant, CancellationToken cancellationToken = default)
    {
        var cachedIconPath = FindCachedIconPath(merchant);
        if (!CanFetch(merchant) || cachedIconPath is not null)
        {
            return cachedIconPath is not null;
        }

        var failedMarkerPath = GetFailedMarkerPath(merchant);
        if (HasRecentFailedMarker(failedMarkerPath))
        {
            return false;
        }

        Directory.CreateDirectory(CacheFolder);

        foreach (var uri in GetFaviconUris(merchant))
        {
            try
            {
                using var response = await HttpClient.GetAsync(uri, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length == 0 || bytes.Length > 256 * 1024)
                {
                    continue;
                }

                var extension = GetExtension(bytes);
                if (extension is null)
                {
                    continue;
                }

                RemoveCachedIconFiles(merchant);
                var iconPath = Path.Combine(CacheFolder, $"{GetCacheStem(merchant)}{extension}");
                await File.WriteAllBytesAsync(iconPath, bytes, cancellationToken);
                TryDelete(failedMarkerPath);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Try the next favicon source before caching the failure.
            }
        }

        try
        {
            await File.WriteAllTextAsync(failedMarkerPath, DateTimeOffset.Now.ToString("O"), CancellationToken.None);
        }
        catch
        {
            // A marker write failure should not interrupt normal app use.
        }

        return false;
    }

    public int ClearCache()
    {
        if (!Directory.Exists(CacheFolder))
        {
            return 0;
        }

        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(CacheFolder))
        {
            if (!IsCacheFile(path))
            {
                continue;
            }

            TryDelete(path);
            removed++;
        }

        return removed;
    }

    public static bool CanFetch(MerchantKind merchant)
    {
        return GetFaviconUris(merchant).Length > 0;
    }

    public static bool TryRecognizeMerchantFromLink(string link, out MerchantKind merchant)
    {
        merchant = MerchantKind.Unknown;
        if (!Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        foreach (var pair in MerchantDomains)
        {
            var brand = pair.Value[..pair.Value.IndexOf('.')];
            if (uri.Host.Contains($"{brand}.", StringComparison.OrdinalIgnoreCase))
            {
                merchant = pair.Key;
                return true;
            }
        }

        return false;
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OrderTrackerDesktop/1.0");
        return httpClient;
    }

    private static Uri[] GetFaviconUris(MerchantKind merchant)
    {
        return MerchantDomains.TryGetValue(merchant, out var domain)
            ? new[] { new Uri($"https://www.{domain}/favicon.ico") }
            : Array.Empty<Uri>();
    }

    private static string GetCacheStem(MerchantKind merchant)
    {
        return MerchantDomains.TryGetValue(merchant, out var domain)
            ? domain[..domain.IndexOf('.')]
            : string.Empty;
    }

    private string GetFailedMarkerPath(MerchantKind merchant)
    {
        return Path.Combine(CacheFolder, $"{GetCacheStem(merchant)}.failed");
    }

    private static string? GetExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }))
        {
            return ".png";
        }

        if (bytes.StartsWith(new byte[] { 0x00, 0x00, 0x01, 0x00 }))
        {
            return ".ico";
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return ".jpg";
        }

        if (bytes.StartsWith("GIF8"u8))
        {
            return ".gif";
        }

        return bytes.StartsWith("BM"u8) ? ".bmp" : null;
    }

    private static bool HasRecentFailedMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return false;
        }

        try
        {
            if (File.GetLastWriteTimeUtc(markerPath) >= DateTime.UtcNow.AddHours(-24))
            {
                return true;
            }
        }
        catch
        {
            // Treat unreadable markers as stale so the fetch can be retried.
        }

        TryDelete(markerPath);
        return false;
    }

    private void RemoveCachedIconFiles(MerchantKind merchant)
    {
        var stem = GetCacheStem(merchant);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return;
        }

        foreach (var extension in SupportedExtensions)
        {
            TryDelete(Path.Combine(CacheFolder, $"{stem}{extension}"));
        }
    }

    private static bool IsCacheFile(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
            extension.Equals(".failed", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cache cleanup should not interrupt normal app use.
        }
    }
}
