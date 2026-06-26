using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OrderTracker.Desktop.Models;

namespace OrderTracker.Desktop.Services;

public sealed class BrowserLauncher
{
    private const int SwRestore = 9;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const int MinimumRememberedWindowWidth = 320;
    private const int MinimumRememberedWindowHeight = 240;
    private const int CascadeOffsetX = 32;
    private const int CascadeOffsetY = 32;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    private readonly Dictionary<string, BrowserWindowReference> _openLinkWindows = new(StringComparer.OrdinalIgnoreCase);
    private IntPtr _lastActiveLinkWindowHandle;

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

    public void CaptureTrackedLinkWindowBounds(AppSettings settings)
    {
        RememberLastActiveWindowBounds(settings);
    }

    private string OpenUrlNormally(Uri uri, AppSettings settings)
    {
        var customBrowserPath = settings.CustomBrowserPath.Trim();
        if (settings.BrowserPreference == BrowserPreference.Custom &&
            !File.Exists(customBrowserPath))
        {
            return "Custom browser path does not exist.";
        }

        var browserPath = ResolveSingleLinkBrowserPath(settings, out var browserName, out var launchKind);

        try
        {
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });

                return $"Opened {uri.Host}.";
            }

            return OpenSingleLinkWindow(
                uri,
                browserPath,
                launchKind,
                sessionDirectory: null,
                settings,
                windowKey: BuildWindowKey(browserPath, sessionDirectory: null, uri),
                openedMessage: $"Opened {uri.Host} in {browserName}.");
        }
        catch (Exception ex)
        {
            return $"Could not open link: {ex.Message}";
        }
    }

    private string OpenUrlInAccountSession(Uri uri, AppSettings settings, BrowserSessionContext sessionContext)
    {
        var browserPath = ResolveAccountSessionBrowserPath(settings, out var browserName);
        if (string.IsNullOrWhiteSpace(browserPath))
        {
            return "No supported account session browser is available. Select Chrome, Edge, Brave, or a custom Chromium browser in Settings.";
        }

        var sessionDirectory = GetSessionDirectory(sessionContext);

        try
        {
            var accountName = string.IsNullOrWhiteSpace(sessionContext.AccountDisplayName)
                ? "account"
                : sessionContext.AccountDisplayName.Trim();

            return OpenSingleLinkWindow(
                uri,
                browserPath,
                BrowserLaunchKind.ChromiumApp,
                sessionDirectory,
                settings,
                BuildWindowKey(browserPath, sessionDirectory, uri),
                $"Opened {sessionContext.Merchant} for {accountName} in {browserName} account session.");
        }
        catch (Exception ex)
        {
            return $"Could not open account session: {ex.Message}";
        }
    }

    private string OpenSingleLinkWindow(
        Uri uri,
        string browserPath,
        BrowserLaunchKind launchKind,
        string? sessionDirectory,
        AppSettings settings,
        string windowKey,
        string openedMessage)
    {
        if (TryActivateExistingWindow(windowKey, uri, settings, out var activationMessage))
        {
            return activationMessage;
        }

        RememberLastActiveWindowBounds(settings);

        if (!string.IsNullOrWhiteSpace(sessionDirectory))
        {
            Directory.CreateDirectory(sessionDirectory);
        }

        var existingWindows = CaptureVisibleWindowHandles();
        var startInfo = new ProcessStartInfo
        {
            FileName = browserPath,
            UseShellExecute = false
        };

        var rememberedBounds = GetRememberedWindowBounds(settings);
        var launchBounds = GetNextLaunchBounds(rememberedBounds);
        AddBrowserArguments(startInfo, launchKind, uri, sessionDirectory, launchBounds);

        var process = Process.Start(startInfo);
        var windowHandle = WaitForLaunchedWindow(process, browserPath, existingWindows, TimeSpan.FromSeconds(3));
        if (process is not null || windowHandle != IntPtr.Zero)
        {
            _openLinkWindows[windowKey] = new BrowserWindowReference(process, windowHandle);
        }

        if (windowHandle != IntPtr.Zero)
        {
            ApplyWindowBounds(windowHandle, launchBounds);
            _lastActiveLinkWindowHandle = windowHandle;
            RememberWindowBounds(windowHandle, settings);
        }

        return openedMessage;
    }

    private bool TryActivateExistingWindow(string windowKey, Uri uri, AppSettings settings, out string message)
    {
        message = string.Empty;

        if (!_openLinkWindows.TryGetValue(windowKey, out var reference))
        {
            return false;
        }

        try
        {
            var windowHandle = reference.WindowHandle;
            if (windowHandle != IntPtr.Zero)
            {
                if (!IsWindow(windowHandle))
                {
                    _openLinkWindows.Remove(windowKey);
                    return false;
                }

                BringWindowToFront(windowHandle);
                _lastActiveLinkWindowHandle = windowHandle;
                RememberWindowBounds(windowHandle, settings);
                message = $"Brought existing {uri.Host} window to the front.";
                return true;
            }

            if (reference.Process is null)
            {
                _openLinkWindows.Remove(windowKey);
                return false;
            }

            reference.Process.Refresh();

            if (reference.Process.HasExited)
            {
                _openLinkWindows.Remove(windowKey);
                return false;
            }

            windowHandle = WaitForMainWindowHandle(reference.Process, TimeSpan.FromMilliseconds(750));
            reference.WindowHandle = windowHandle;

            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                _openLinkWindows.Remove(windowKey);
                return false;
            }

            BringWindowToFront(windowHandle);
            _lastActiveLinkWindowHandle = windowHandle;
            RememberWindowBounds(windowHandle, settings);
            message = $"Brought existing {uri.Host} window to the front.";
            return true;
        }
        catch
        {
            _openLinkWindows.Remove(windowKey);
            return false;
        }
    }

    private static void BringWindowToFront(IntPtr windowHandle)
    {
        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, SwRestore);
        }

        SetForegroundWindow(windowHandle);
    }

    private void RememberLastActiveWindowBounds(AppSettings settings)
    {
        if (_lastActiveLinkWindowHandle != IntPtr.Zero && IsWindow(_lastActiveLinkWindowHandle))
        {
            RememberWindowBounds(_lastActiveLinkWindowHandle, settings);
        }
    }

    private static void RememberWindowBounds(IntPtr windowHandle, AppSettings settings)
    {
        if (!TryGetWindowBounds(windowHandle, out var left, out var top, out var width, out var height))
        {
            return;
        }

        if (!IsUsableWindowSize(width, height))
        {
            return;
        }

        if (settings.BrowserLinkWindowWidth != width)
        {
            settings.BrowserLinkWindowWidth = width;
        }

        if (settings.BrowserLinkWindowHeight != height)
        {
            settings.BrowserLinkWindowHeight = height;
        }

        if (settings.BrowserLinkWindowLeft != left)
        {
            settings.BrowserLinkWindowLeft = left;
        }

        if (settings.BrowserLinkWindowTop != top)
        {
            settings.BrowserLinkWindowTop = top;
        }
    }

    private BrowserWindowBounds? GetNextLaunchBounds(BrowserWindowBounds? rememberedBounds)
    {
        if (!TryGetTrackedOpenWindowBounds(out var trackedBounds))
        {
            return rememberedBounds;
        }

        return CascadeWindowBounds(trackedBounds);
    }

    private bool TryGetTrackedOpenWindowBounds(out BrowserWindowBounds bounds)
    {
        if (TryGetUsableWindowBounds(_lastActiveLinkWindowHandle, out bounds))
        {
            return true;
        }

        foreach (var pair in _openLinkWindows.ToArray())
        {
            if (pair.Value.WindowHandle != IntPtr.Zero)
            {
                if (TryGetUsableWindowBounds(pair.Value.WindowHandle, out bounds))
                {
                    _lastActiveLinkWindowHandle = pair.Value.WindowHandle;
                    return true;
                }

                _openLinkWindows.Remove(pair.Key);
                continue;
            }

            if (pair.Value.Process is null)
            {
                _openLinkWindows.Remove(pair.Key);
                continue;
            }

            try
            {
                pair.Value.Process.Refresh();
                if (!pair.Value.Process.HasExited)
                {
                    var windowHandle = WaitForMainWindowHandle(pair.Value.Process, TimeSpan.FromMilliseconds(250));
                    pair.Value.WindowHandle = windowHandle;

                    if (TryGetUsableWindowBounds(windowHandle, out bounds))
                    {
                        _lastActiveLinkWindowHandle = windowHandle;
                        return true;
                    }
                }
            }
            catch
            {
                _openLinkWindows.Remove(pair.Key);
            }
        }

        _lastActiveLinkWindowHandle = IntPtr.Zero;
        bounds = default;
        return false;
    }

    private static BrowserWindowBounds? GetRememberedWindowBounds(AppSettings settings)
    {
        if (settings.BrowserLinkWindowWidth is not { } widthValue ||
            settings.BrowserLinkWindowHeight is not { } heightValue)
        {
            return null;
        }

        var width = (int)Math.Round(widthValue);
        var height = (int)Math.Round(heightValue);

        if (!IsUsableWindowSize(width, height))
        {
            return null;
        }

        int? left = settings.BrowserLinkWindowLeft is { } leftValue
            ? (int)Math.Round(leftValue)
            : null;
        int? top = settings.BrowserLinkWindowTop is { } topValue
            ? (int)Math.Round(topValue)
            : null;

        return new BrowserWindowBounds(left, top, width, height);
    }

    private static BrowserWindowBounds CascadeWindowBounds(BrowserWindowBounds baseBounds)
    {
        var left = (baseBounds.Left ?? 0) + CascadeOffsetX;
        var top = (baseBounds.Top ?? 0) + CascadeOffsetY;
        return KeepBoundsOnVirtualScreen(new BrowserWindowBounds(left, top, baseBounds.Width, baseBounds.Height), baseBounds);
    }

    private static BrowserWindowBounds KeepBoundsOnVirtualScreen(BrowserWindowBounds candidateBounds, BrowserWindowBounds fallbackBounds)
    {
        var screenLeft = GetSystemMetrics(SmXVirtualScreen);
        var screenTop = GetSystemMetrics(SmYVirtualScreen);
        var screenWidth = GetSystemMetrics(SmCxVirtualScreen);
        var screenHeight = GetSystemMetrics(SmCyVirtualScreen);

        if (screenWidth <= 0 || screenHeight <= 0 ||
            candidateBounds.Left is not { } candidateLeft ||
            candidateBounds.Top is not { } candidateTop)
        {
            return candidateBounds;
        }

        var screenRight = screenLeft + screenWidth;
        var screenBottom = screenTop + screenHeight;
        var left = candidateLeft;
        var top = candidateTop;

        if (left + candidateBounds.Width > screenRight)
        {
            left = fallbackBounds.Left ?? screenLeft;
        }

        if (top + candidateBounds.Height > screenBottom)
        {
            top = fallbackBounds.Top ?? screenTop;
        }

        return new BrowserWindowBounds(left, top, candidateBounds.Width, candidateBounds.Height);
    }

    private static void ApplyWindowBounds(IntPtr windowHandle, BrowserWindowBounds? windowBounds)
    {
        if (windowBounds is null || !IsWindow(windowHandle))
        {
            return;
        }

        var bounds = windowBounds.Value;
        var flags = SwpNoZOrder | SwpNoActivate;
        var left = 0;
        var top = 0;

        if (bounds.Left is { } rememberedLeft && bounds.Top is { } rememberedTop)
        {
            left = rememberedLeft;
            top = rememberedTop;
        }
        else
        {
            flags |= SwpNoMove;
        }

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            left,
            top,
            bounds.Width,
            bounds.Height,
            flags);
    }

    private static bool TryGetWindowBounds(IntPtr windowHandle, out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;

        if (!GetWindowRect(windowHandle, out var rect))
        {
            return false;
        }

        left = rect.Left;
        top = rect.Top;
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return true;
    }

    private static bool TryGetUsableWindowBounds(IntPtr windowHandle, out BrowserWindowBounds bounds)
    {
        bounds = default;

        if (windowHandle == IntPtr.Zero ||
            !IsWindow(windowHandle) ||
            !TryGetWindowBounds(windowHandle, out var left, out var top, out var width, out var height) ||
            !IsUsableWindowSize(width, height))
        {
            return false;
        }

        bounds = new BrowserWindowBounds(left, top, width, height);
        return true;
    }

    private static bool IsUsableWindowSize(int width, int height)
    {
        return width >= MinimumRememberedWindowWidth &&
               height >= MinimumRememberedWindowHeight;
    }

    private static IntPtr WaitForLaunchedWindow(Process? process, string browserPath, HashSet<IntPtr> existingWindows, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        do
        {
            if (process is not null)
            {
                process.Refresh();

                if (!process.HasExited)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainWindowHandle;
                    }

                    var processWindowHandle = FindVisibleWindowForProcess(process.Id);
                    if (processWindowHandle != IntPtr.Zero)
                    {
                        return processWindowHandle;
                    }
                }
            }

            var newWindowHandle = FindNewVisibleBrowserWindow(existingWindows, browserPath);
            if (newWindowHandle != IntPtr.Zero)
            {
                return newWindowHandle;
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        return IntPtr.Zero;
    }

    private static void AddBrowserArguments(
        ProcessStartInfo startInfo,
        BrowserLaunchKind launchKind,
        Uri uri,
        string? sessionDirectory,
        BrowserWindowBounds? rememberedBounds)
    {
        switch (launchKind)
        {
            case BrowserLaunchKind.ChromiumApp:
                if (!string.IsNullOrWhiteSpace(sessionDirectory))
                {
                    startInfo.ArgumentList.Add($"--user-data-dir={sessionDirectory}");
                }

                startInfo.ArgumentList.Add("--no-first-run");
                startInfo.ArgumentList.Add("--no-default-browser-check");
                startInfo.ArgumentList.Add("--disable-session-crashed-bubble");
                if (rememberedBounds is { } chromiumBounds)
                {
                    startInfo.ArgumentList.Add($"--window-size={chromiumBounds.Width},{chromiumBounds.Height}");
                    if (chromiumBounds.Left is { } chromiumLeft &&
                        chromiumBounds.Top is { } chromiumTop)
                    {
                        startInfo.ArgumentList.Add($"--window-position={chromiumLeft},{chromiumTop}");
                    }
                }

                startInfo.ArgumentList.Add($"--app={uri.AbsoluteUri}");
                break;

            case BrowserLaunchKind.FirefoxWindow:
                startInfo.ArgumentList.Add("-new-window");
                startInfo.ArgumentList.Add(uri.AbsoluteUri);
                break;

            default:
                startInfo.ArgumentList.Add(uri.AbsoluteUri);
                break;
        }
    }

    private static IntPtr WaitForMainWindowHandle(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        do
        {
            process.Refresh();

            if (process.HasExited)
            {
                return IntPtr.Zero;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }

            var windowHandle = FindVisibleWindowForProcess(process.Id);
            if (windowHandle != IntPtr.Zero)
            {
                return windowHandle;
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        return IntPtr.Zero;
    }

    private static IntPtr FindVisibleWindowForProcess(int processId)
    {
        var result = IntPtr.Zero;

        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }

            result = windowHandle;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static HashSet<IntPtr> CaptureVisibleWindowHandles()
    {
        var handles = new HashSet<IntPtr>();

        EnumWindows((windowHandle, _) =>
        {
            if (IsWindowVisible(windowHandle))
            {
                handles.Add(windowHandle);
            }

            return true;
        }, IntPtr.Zero);

        return handles;
    }

    private static IntPtr FindNewVisibleBrowserWindow(HashSet<IntPtr> existingWindows, string browserPath)
    {
        var result = IntPtr.Zero;
        var browserProcessName = Path.GetFileNameWithoutExtension(browserPath);

        EnumWindows((windowHandle, _) =>
        {
            if (existingWindows.Contains(windowHandle) || !IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var windowProcessId);
            if (!IsProcessName(windowProcessId, browserProcessName))
            {
                return true;
            }

            result = windowHandle;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsProcessName(int processId, string expectedProcessName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
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

    private static string ResolveSingleLinkBrowserPath(AppSettings settings, out string browserName, out BrowserLaunchKind launchKind)
    {
        browserName = GetBrowserName(settings.BrowserPreference);
        launchKind = BrowserLaunchKind.StandardWindow;

        if (settings.BrowserPreference == BrowserPreference.Default)
        {
            var defaultPreference = ResolveDefaultBrowserPreference();
            if (defaultPreference.HasValue)
            {
                foreach (var candidate in GetBrowserCandidates(defaultPreference.Value))
                {
                    if (File.Exists(candidate))
                    {
                        browserName = GetBrowserName(defaultPreference.Value);
                        launchKind = GetLaunchKind(defaultPreference.Value, candidate);
                        return candidate;
                    }
                }
            }

            return string.Empty;
        }

        var browserPath = ResolveBrowserPath(settings);
        if (string.IsNullOrWhiteSpace(browserPath))
        {
            return string.Empty;
        }

        launchKind = GetLaunchKind(settings.BrowserPreference, browserPath);
        return browserPath;
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

    private static BrowserLaunchKind GetLaunchKind(BrowserPreference preference, string browserPath)
    {
        if (preference == BrowserPreference.Firefox || IsFirefoxBrowserPath(browserPath))
        {
            return BrowserLaunchKind.FirefoxWindow;
        }

        if (preference is BrowserPreference.Chrome or BrowserPreference.Edge or BrowserPreference.Brave ||
            IsChromiumBrowserPath(browserPath))
        {
            return BrowserLaunchKind.ChromiumApp;
        }

        return BrowserLaunchKind.StandardWindow;
    }

    private static BrowserPreference? ResolveDefaultBrowserPreference()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var progId = key?.GetValue("ProgId") as string;
            if (string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

            if (progId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase))
            {
                return BrowserPreference.Edge;
            }

            if (progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
            {
                return BrowserPreference.Chrome;
            }

            if (progId.Contains("Brave", StringComparison.OrdinalIgnoreCase))
            {
                return BrowserPreference.Brave;
            }

            if (progId.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
            {
                return BrowserPreference.Firefox;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool IsChromiumBrowserPath(string browserPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(browserPath);
        return fileName.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("brave", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("chromium", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFirefoxBrowserPath(string browserPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(browserPath);
        return fileName.Contains("firefox", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildWindowKey(string browserPath, string? sessionDirectory, Uri uri)
    {
        return string.Join(
            "|",
            browserPath.Trim().ToLowerInvariant(),
            (sessionDirectory ?? string.Empty).Trim().ToLowerInvariant(),
            uri.AbsoluteUri.Trim().ToLowerInvariant());
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

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly record struct BrowserWindowBounds(int? Left, int? Top, int Width, int Height);

    private sealed class BrowserWindowReference
    {
        public BrowserWindowReference(Process? process, IntPtr windowHandle)
        {
            Process = process;
            WindowHandle = windowHandle;
        }

        public Process? Process { get; }

        public IntPtr WindowHandle { get; set; }
    }

    private enum BrowserLaunchKind
    {
        ChromiumApp,
        FirefoxWindow,
        StandardWindow
    }
}

public sealed class BrowserSessionContext
{
    public MerchantKind Merchant { get; init; } = MerchantKind.Unknown;

    public string AccountKey { get; init; } = string.Empty;

    public string AccountDisplayName { get; init; } = string.Empty;
}
