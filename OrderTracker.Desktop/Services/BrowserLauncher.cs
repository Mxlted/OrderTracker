using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class BrowserLauncher
{
    public string OpenUrl(string url, AppSettings settings, BrowserSessionContext? sessionContext = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "No valid web link is available for that action.";
        }

        if (settings.UseAccountBrowserSessions &&
            sessionContext is not null &&
            !string.IsNullOrWhiteSpace(sessionContext.AccountKey))
        {
            return OpenUrlInAccountSession(uri, settings, sessionContext);
        }

        return OpenUrlNormally(uri, settings);
    }

    private static string OpenUrlNormally(Uri uri, AppSettings settings)
    {
        var customBrowserPath = settings.CustomBrowserPath.Trim();
        if (settings.BrowserPreference == BrowserPreference.Custom &&
            !File.Exists(customBrowserPath))
        {
            return "Custom browser path does not exist.";
        }

        var browserPath = ResolveBrowserPath(settings);

        try
        {
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = browserPath,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(uri.AbsoluteUri);
                Process.Start(startInfo);
            }

            return $"Opened {uri.Host}.";
        }
        catch (Exception ex)
        {
            return $"Could not open link: {ex.Message}";
        }
    }

    private static string OpenUrlInAccountSession(Uri uri, AppSettings settings, BrowserSessionContext sessionContext)
    {
        var browserPath = ResolveAccountSessionBrowserPath(settings, out var browserName);
        if (string.IsNullOrWhiteSpace(browserPath))
        {
            return "No supported account session browser is available. Select Chrome, Edge, Brave, or a custom Chromium browser in Settings.";
        }

        var sessionDirectory = GetSessionDirectory(sessionContext);

        try
        {
            Directory.CreateDirectory(sessionDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = browserPath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add($"--user-data-dir={sessionDirectory}");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--new-window");
            startInfo.ArgumentList.Add(uri.AbsoluteUri);
            Process.Start(startInfo);

            var accountName = string.IsNullOrWhiteSpace(sessionContext.AccountDisplayName)
                ? "account"
                : sessionContext.AccountDisplayName.Trim();

            return $"Opened {sessionContext.Merchant} for {accountName} in {browserName} account session.";
        }
        catch (Exception ex)
        {
            return $"Could not open account session: {ex.Message}";
        }
    }

    private static string ResolveBrowserPath(AppSettings settings)
    {
        if (settings.BrowserPreference == BrowserPreference.Default)
        {
            return string.Empty;
        }

        if (settings.BrowserPreference == BrowserPreference.Custom)
        {
            var customBrowserPath = settings.CustomBrowserPath.Trim();
            return File.Exists(customBrowserPath) ? customBrowserPath : string.Empty;
        }

        foreach (var candidate in GetBrowserCandidates(settings.BrowserPreference))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string ResolveAccountSessionBrowserPath(AppSettings settings, out string browserName)
    {
        browserName = "browser";

        if (settings.BrowserPreference == BrowserPreference.Custom)
        {
            var customBrowserPath = settings.CustomBrowserPath.Trim();
            if (File.Exists(customBrowserPath))
            {
                browserName = "custom browser";
                return customBrowserPath;
            }

            return string.Empty;
        }

        if (settings.BrowserPreference is BrowserPreference.Chrome or BrowserPreference.Edge or BrowserPreference.Brave)
        {
            var preferredPath = ResolveBrowserPath(settings);
            if (!string.IsNullOrWhiteSpace(preferredPath))
            {
                browserName = GetBrowserName(settings.BrowserPreference);
                return preferredPath;
            }
        }

        foreach (var preference in new[] { BrowserPreference.Edge, BrowserPreference.Chrome, BrowserPreference.Brave })
        {
            foreach (var candidate in GetBrowserCandidates(preference))
            {
                if (File.Exists(candidate))
                {
                    browserName = GetBrowserName(preference);
                    return candidate;
                }
            }
        }

        return string.Empty;
    }

    private static string GetSessionDirectory(BrowserSessionContext sessionContext)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var merchantFolder = sessionContext.Merchant.ToString().ToLowerInvariant();
        var sessionKey = ComputeStableKey($"{sessionContext.Merchant}:{sessionContext.AccountKey.Trim().ToLowerInvariant()}");
        return Path.Combine(appData, "OrderTrackerDesktop", "browser-sessions", merchantFolder, sessionKey);
    }

    private static string ComputeStableKey(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string GetBrowserName(BrowserPreference preference)
    {
        return preference switch
        {
            BrowserPreference.Chrome => "Chrome",
            BrowserPreference.Edge => "Edge",
            BrowserPreference.Brave => "Brave",
            BrowserPreference.Firefox => "Firefox",
            BrowserPreference.Custom => "custom browser",
            _ => "browser"
        };
    }

    private static IEnumerable<string> GetBrowserCandidates(BrowserPreference preference)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return preference switch
        {
            BrowserPreference.Chrome => new[]
            {
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
            },
            BrowserPreference.Edge => new[]
            {
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")
            },
            BrowserPreference.Brave => new[]
            {
                Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
            },
            BrowserPreference.Firefox => new[]
            {
                Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe")
            },
            _ => Array.Empty<string>()
        };
    }
}

public sealed class BrowserSessionContext
{
    public MerchantKind Merchant { get; init; } = MerchantKind.Unknown;

    public string AccountKey { get; init; } = string.Empty;

    public string AccountDisplayName { get; init; } = string.Empty;
}
