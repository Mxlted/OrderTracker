using System.IO;
using System.Net.Http;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class MerchantFaviconService
{
    private static readonly string[] SupportedExtensions = { ".png", ".ico", ".jpg", ".jpeg", ".gif", ".bmp" };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public MerchantFaviconService()
    {
        CacheFolder = Path.Combine(GetAppDataFolder(), "merchant-icons");
    }

    public string CacheFolder { get; }

    public int CachedIconCount => AppSettings.ListedMerchants.Count(merchant => FindCachedIconPath(merchant) is not null);

    public static string? FindCachedIconPath(MerchantKind merchant)
    {
        var folder = Path.Combine(GetAppDataFolder(), "merchant-icons");
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
        if (!CanFetch(merchant) || FindCachedIconPath(merchant) is not null || File.Exists(GetFailedMarkerPath(merchant)))
        {
            return FindCachedIconPath(merchant) is not null;
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

                var extension = GetExtension(response.Content.Headers.ContentType?.MediaType, uri);
                if (extension is null)
                {
                    continue;
                }

                RemoveCachedIconFiles(merchant);
                var iconPath = Path.Combine(CacheFolder, $"{GetCacheStem(merchant)}{extension}");
                await File.WriteAllBytesAsync(iconPath, bytes, cancellationToken);
                TryDelete(GetFailedMarkerPath(merchant));
                return true;
            }
            catch
            {
                // The failed marker below prevents repeated network attempts until the cache is cleared.
            }
        }

        await File.WriteAllTextAsync(GetFailedMarkerPath(merchant), DateTimeOffset.Now.ToString("O"), cancellationToken);
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

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OrderTrackerDesktop/1.0");
        return httpClient;
    }

    private static string GetAppDataFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OrderTrackerDesktop");
    }

    private static Uri[] GetFaviconUris(MerchantKind merchant)
    {
        return merchant switch
        {
            MerchantKind.Amazon => new[] { new Uri("https://www.amazon.com/favicon.ico") },
            MerchantKind.Walmart => new[] { new Uri("https://www.walmart.com/favicon.ico") },
            MerchantKind.Target => new[] { new Uri("https://www.target.com/favicon.ico") },
            MerchantKind.BestBuy => new[] { new Uri("https://www.bestbuy.com/favicon.ico") },
            MerchantKind.eBay => new[] { new Uri("https://www.ebay.com/favicon.ico") },
            _ => Array.Empty<Uri>()
        };
    }

    private static string GetCacheStem(MerchantKind merchant)
    {
        return merchant switch
        {
            MerchantKind.BestBuy => "bestbuy",
            MerchantKind.eBay => "ebay",
            MerchantKind.Amazon or MerchantKind.Walmart or MerchantKind.Target => merchant.ToString().ToLowerInvariant(),
            _ => string.Empty
        };
    }

    private string GetFailedMarkerPath(MerchantKind merchant)
    {
        return Path.Combine(CacheFolder, $"{GetCacheStem(merchant)}.failed");
    }

    private static string? GetExtension(string? mediaType, Uri uri)
    {
        var normalizedMediaType = mediaType?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedMediaType) &&
            !normalizedMediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            normalizedMediaType != "application/octet-stream")
        {
            return null;
        }

        var extension = normalizedMediaType switch
        {
            "image/png" => ".png",
            "image/x-icon" or "image/vnd.microsoft.icon" or "image/icon" => ".ico",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => null
        };

        extension ??= Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension) ? extension : null;
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
