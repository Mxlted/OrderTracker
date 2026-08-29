using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OrderTracker.Desktop.Models;
using OrderTracker.Desktop.Utilities;

namespace OrderTracker.Desktop.Services;

public sealed class BrowserLauncher : IDisposable
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

    private readonly ConcurrentDictionary<string, BrowserWindowReference> _openLinkWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _trackingCancellation = new();
    private readonly object _windowActivityLock = new();
    private int _isDisposed;
    private IntPtr _lastActiveLinkWindowHandle;
    private BrowserWindowReference? _lastActiveLinkWindow;
    private BrowserWindowBounds? _lastActiveLinkWindowBounds;

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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _trackingCancellation.Cancel();
        foreach (var pair in _openLinkWindows.ToArray())
        {
            RemoveTrackedWindow(pair.Key, pair.Value);
        }

        lock (_windowActivityLock)
        {
            _lastActiveLinkWindow = null;
            _lastActiveLinkWindowHandle = IntPtr.Zero;
            _lastActiveLinkWindowBounds = null;
        }

        _trackingCancellation.Dispose();
    }

    public string ClearAccountSession(BrowserSessionContext sessionContext)
    {
        if (string.IsNullOrWhiteSpace(sessionContext.AccountKey))
        {
            return "Select an account with an email before clearing its browser session.";
        }

        var sessionDirectory = GetSessionDirectory(sessionContext);
        var accountName = string.IsNullOrWhiteSpace(sessionContext.AccountDisplayName)
            ? "account"
            : sessionContext.AccountDisplayName.Trim();
        var merchantName = sessionContext.Merchant.ToString();

        try
        {
            var hadExistingSession = Directory.Exists(sessionDirectory);
            if (hadExistingSession)
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }

            Directory.CreateDirectory(sessionDirectory);
            RemoveTrackedWindowsForSession(sessionDirectory);

            return hadExistingSession
                ? $"Cleared {merchantName} session for {accountName}. A fresh session will be used the next time it opens."
                : $"Created a fresh {merchantName} session for {accountName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not clear {merchantName} session for {accountName}. Close any open account-session browser windows and try again.";
        }
        catch (Exception ex)
        {
            return $"Could not clear {merchantName} session for {accountName}: {ex.Message}";
        }
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
                $"Opened {EnumDisplayFormatter.Format(sessionContext.Merchant)} for {accountName} in {browserName} account session.");
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
        if (TryActivateExistingWindow(windowKey, uri, out var activationMessage))
        {
            return activationMessage;
        }

        CaptureLastActiveWindowBounds();

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
        var trackingCancellationToken = _trackingCancellation.Token;

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return $"Could not start {Path.GetFileName(browserPath)}: {ex.Message}";
        }

        if (process is null)
        {
            return $"Could not start {Path.GetFileName(browserPath)}.";
        }

        var reference = new BrowserWindowReference(process, IntPtr.Zero, sessionDirectory, launchBounds);
        TrackWindowReference(windowKey, reference);
        MarkWindowActive(reference, IntPtr.Zero, launchBounds);
        _ = CompleteWindowTrackingAsync(
            windowKey,
            reference,
            browserPath,
            existingWindows,
            launchBounds,
            trackingCancellationToken);

        return openedMessage;
    }

    private bool TryActivateExistingWindow(string windowKey, Uri uri, out string message)
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
                    RemoveTrackedWindow(windowKey, reference);
                    return false;
                }

                var broughtToFront = BringWindowToFront(windowHandle);
                var bounds = reference.CanManageBounds ? GetUsableWindowBounds(windowHandle) : null;
                MarkWindowActive(reference, windowHandle, bounds);
                message = broughtToFront
                    ? $"Brought existing {uri.Host} window to the front."
                    : "Window is already open.";
                return true;
            }

            if (reference.Process is null)
            {
                RemoveTrackedWindow(windowKey, reference);
                return false;
            }

            reference.Process.Refresh();

            if (reference.Process.HasExited)
            {
                RemoveTrackedWindow(windowKey, reference);
                return false;
            }

            windowHandle = GetMainWindowHandleIfReady(reference.Process);
            reference.WindowHandle = windowHandle;

            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                if (DateTime.UtcNow - reference.LaunchedAtUtc >= TimeSpan.FromSeconds(15))
                {
                    RemoveTrackedWindow(windowKey, reference);
                    return false;
                }

                message = $"Still opening {uri.Host}.";
                return true;
            }

            var activated = BringWindowToFront(windowHandle);
            MarkWindowActive(reference, windowHandle, GetUsableWindowBounds(windowHandle));
            message = activated
                ? $"Brought existing {uri.Host} window to the front."
                : "Window is already open.";
            return true;
        }
        catch
        {
            RemoveTrackedWindow(windowKey, reference);
            return false;
        }
    }

    private void RemoveTrackedWindow(
        string windowKey,
        BrowserWindowReference? expectedReference = null)
    {
        if (!_openLinkWindows.TryGetValue(windowKey, out var reference) ||
            (expectedReference is not null && !ReferenceEquals(reference, expectedReference)) ||
            !_openLinkWindows.TryRemove(new KeyValuePair<string, BrowserWindowReference>(windowKey, reference)))
        {
            return;
        }

        lock (_windowActivityLock)
        {
            if (ReferenceEquals(reference, _lastActiveLinkWindow))
            {
                _lastActiveLinkWindowHandle = IntPtr.Zero;
                if (reference.LastKnownBounds is { } bounds)
                {
                    _lastActiveLinkWindowBounds = bounds;
                }
            }
        }

        reference.Dispose();
    }

    private void MarkWindowActive(
        BrowserWindowReference reference,
        IntPtr windowHandle,
        BrowserWindowBounds? bounds,
        bool clearBounds = false)
    {
        lock (_windowActivityLock)
        {
            if (windowHandle != IntPtr.Zero)
            {
                reference.WindowHandle = windowHandle;
            }

            if (clearBounds)
            {
                reference.LastKnownBounds = null;
                _lastActiveLinkWindowBounds = null;
            }
            else if (bounds is { } currentBounds)
            {
                reference.LastKnownBounds = currentBounds;
                _lastActiveLinkWindowBounds = currentBounds;
            }

            _lastActiveLinkWindow = reference;
            _lastActiveLinkWindowHandle = windowHandle;
        }
    }

    private void UpdateLastKnownBounds(BrowserWindowReference? reference, BrowserWindowBounds? bounds)
    {
        if (reference is null || bounds is not { } currentBounds)
        {
            return;
        }

        lock (_windowActivityLock)
        {
            reference.LastKnownBounds = currentBounds;
            if (ReferenceEquals(reference, _lastActiveLinkWindow))
            {
                _lastActiveLinkWindowBounds = currentBounds;
            }
        }
    }

    private void TrackWindowReference(string windowKey, BrowserWindowReference reference)
    {
        while (true)
        {
            if (_openLinkWindows.TryGetValue(windowKey, out var existingReference))
            {
                if (_openLinkWindows.TryUpdate(windowKey, reference, existingReference))
                {
                    existingReference.Dispose();
                    return;
                }

                continue;
            }

            if (_openLinkWindows.TryAdd(windowKey, reference))
            {
                return;
            }
        }
    }

    private async Task CompleteWindowTrackingAsync(
        string windowKey,
        BrowserWindowReference reference,
        string browserPath,
        HashSet<IntPtr> existingWindows,
        BrowserWindowBounds? launchBounds,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await WaitForLaunchedWindowAsync(
                reference.Process,
                browserPath,
                existingWindows,
                TimeSpan.FromSeconds(3),
                cancellationToken).ConfigureAwait(false);
            if (result.WindowHandle == IntPtr.Zero ||
                !_openLinkWindows.TryGetValue(windowKey, out var currentReference) ||
                !ReferenceEquals(currentReference, reference))
            {
                return;
            }

            reference.WindowHandle = result.WindowHandle;
            reference.CanManageBounds = result.CanManageBounds;
            if (result.CanManageBounds)
            {
                ApplyWindowBounds(result.WindowHandle, launchBounds);
            }

            var bounds = result.CanManageBounds
                ? GetUsableWindowBounds(result.WindowHandle) ?? launchBounds
                : null;
            MarkWindowActive(
                reference,
                result.WindowHandle,
                bounds,
                clearBounds: !result.CanManageBounds);
            _ = MonitorTrackedWindowAsync(windowKey, reference, cancellationToken);
        }
        catch
        {
            if (_openLinkWindows.TryGetValue(windowKey, out var currentReference) &&
                ReferenceEquals(currentReference, reference))
            {
                RemoveTrackedWindow(windowKey, reference);
            }
        }
    }

    private async Task MonitorTrackedWindowAsync(
        string windowKey,
        BrowserWindowReference reference,
        CancellationToken cancellationToken)
    {
        try
        {
            while (_openLinkWindows.TryGetValue(windowKey, out var currentReference) &&
                   ReferenceEquals(currentReference, reference) &&
                   reference.WindowHandle != IntPtr.Zero &&
                   IsWindow(reference.WindowHandle))
            {
                var bounds = reference.CanManageBounds
                    ? GetUsableWindowBounds(reference.WindowHandle)
                    : null;
                if (reference.CanManageBounds)
                {
                    UpdateLastKnownBounds(reference, bounds);
                }

                if (GetForegroundWindow() == reference.WindowHandle)
                {
                    MarkWindowActive(reference, reference.WindowHandle, bounds);
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Window monitoring is best-effort; the cached bounds still remain usable.
        }
        finally
        {
            RemoveTrackedWindow(windowKey, reference);
        }
    }

    private void RemoveTrackedWindowsForSession(string sessionDirectory)
    {
        var normalizedSessionDirectory = NormalizeWindowKeyPart(sessionDirectory);
        foreach (var pair in _openLinkWindows.ToArray())
        {
            if (string.Equals(
                    NormalizeWindowKeyPart(pair.Value.SessionDirectory),
                    normalizedSessionDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                RemoveTrackedWindow(pair.Key);
            }
        }
    }

    private static bool BringWindowToFront(IntPtr windowHandle)
    {
        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, SwRestore);
        }

        if (SetForegroundWindow(windowHandle))
        {
            return true;
        }

        ShowWindow(windowHandle, SwRestore);
        return SetForegroundWindow(windowHandle);
    }

    private void RememberLastActiveWindowBounds(AppSettings settings)
    {
        CaptureLastActiveWindowBounds();

        BrowserWindowBounds? bounds;
        lock (_windowActivityLock)
        {
            bounds = _lastActiveLinkWindowBounds;
        }

        if (bounds is { } cachedBounds)
        {
            RememberWindowBounds(cachedBounds, settings);
        }
    }

    private void CaptureLastActiveWindowBounds()
    {
        BrowserWindowReference? reference;
        IntPtr windowHandle;
        lock (_windowActivityLock)
        {
            reference = _lastActiveLinkWindow;
            windowHandle = _lastActiveLinkWindowHandle;
        }

        if (reference is not { CanManageBounds: true })
        {
            return;
        }

        var liveBounds = GetUsableWindowBounds(windowHandle);
        if (liveBounds is { } currentBounds)
        {
            UpdateLastKnownBounds(reference, currentBounds);
        }
    }

    private static void RememberWindowBounds(BrowserWindowBounds bounds, AppSettings settings)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var width = bounds.Width;
        var height = bounds.Height;

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
        BrowserWindowBounds? lastActiveBounds;
        lock (_windowActivityLock)
        {
            lastActiveBounds = _lastActiveLinkWindowBounds;
        }

        if (lastActiveBounds is not { } trackedBounds)
        {
            return rememberedBounds;
        }

        return CascadeWindowBounds(trackedBounds);
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

        if (left < screenLeft)
        {
            left = screenLeft;
        }

        if (top < screenTop)
        {
            top = screenTop;
        }

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

    private static BrowserWindowBounds? GetUsableWindowBounds(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero ||
            !IsWindow(windowHandle) ||
            !TryGetWindowBounds(windowHandle, out var left, out var top, out var width, out var height) ||
            !IsUsableWindowSize(width, height))
        {
            return null;
        }

        return new BrowserWindowBounds(left, top, width, height);
    }

    private static bool IsUsableWindowSize(int width, int height)
    {
        return width >= MinimumRememberedWindowWidth &&
               height >= MinimumRememberedWindowHeight;
    }

    private static async Task<LaunchedWindowResult> WaitForLaunchedWindowAsync(
        Process? process,
        string browserPath,
        HashSet<IntPtr> existingWindows,
        TimeSpan timeout,
        CancellationToken cancellationToken)
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
                        return new LaunchedWindowResult(process.MainWindowHandle, CanManageBounds: true);
                    }

                    var processWindowHandle = FindVisibleWindowForProcess(process.Id);
                    if (processWindowHandle != IntPtr.Zero)
                    {
                        return new LaunchedWindowResult(processWindowHandle, CanManageBounds: true);
                    }
                }
            }

            var newWindowHandle = FindNewVisibleBrowserWindow(existingWindows, browserPath);
            if (newWindowHandle != IntPtr.Zero)
            {
                var launchedProcessHasOwnWindow = process is not null &&
                                                  GetMainWindowHandleIfReady(process) != IntPtr.Zero;
                return new LaunchedWindowResult(
                    newWindowHandle,
                    CanManageBounds: !launchedProcessHasOwnWindow);
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return default;
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

    private static IntPtr GetMainWindowHandleIfReady(Process process)
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

        return FindVisibleWindowForProcess(process.Id);
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
            if (existingWindows.Contains(windowHandle) ||
                !IsWindowVisible(windowHandle) ||
                !HasWindowTitle(windowHandle))
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

    private static bool HasWindowTitle(IntPtr windowHandle)
    {
        var titleLength = GetWindowTextLength(windowHandle);
        if (titleLength <= 0)
        {
            return false;
        }

        var title = new StringBuilder(titleLength + 1);
        return GetWindowText(windowHandle, title, title.Capacity) > 0 && title.Length > 0;
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
            NormalizeWindowKeyPart(browserPath),
            NormalizeWindowKeyPart(sessionDirectory),
            NormalizeWindowKeyPart(uri.AbsoluteUri));
    }

    private static string NormalizeWindowKeyPart(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string GetSessionDirectory(BrowserSessionContext sessionContext)
    {
        var merchantFolder = sessionContext.Merchant.ToString().ToLowerInvariant();
        var sessionKey = ComputeStableKey($"{sessionContext.Merchant}:{sessionContext.AccountKey.Trim().ToLowerInvariant()}");
        return Path.Combine(AppPaths.BrowserSessionsFolder, merchantFolder, sessionKey);
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
        var executableName = preference switch
        {
            BrowserPreference.Chrome => "chrome.exe",
            BrowserPreference.Edge => "msedge.exe",
            BrowserPreference.Brave => "brave.exe",
            BrowserPreference.Firefox => "firefox.exe",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(executableName))
        {
            foreach (var registryRoot in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                var registeredPath = GetRegisteredBrowserPath(registryRoot, executableName);
                if (!string.IsNullOrWhiteSpace(registeredPath))
                {
                    yield return registeredPath;
                }
            }
        }

        var fallbackCandidates = preference switch
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
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")
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

        foreach (var candidate in fallbackCandidates)
        {
            yield return candidate;
        }
    }

    private static string? GetRegisteredBrowserPath(RegistryKey registryRoot, string executableName)
    {
        try
        {
            using var key = registryRoot.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
            return key?.GetValue(null) is string path
                ? path.Trim().Trim('"')
                : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect lpRect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
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

    private readonly record struct LaunchedWindowResult(IntPtr WindowHandle, bool CanManageBounds);

    private sealed class BrowserWindowReference : IDisposable
    {
        public BrowserWindowReference(
            Process? process,
            IntPtr windowHandle,
            string? sessionDirectory,
            BrowserWindowBounds? lastKnownBounds)
        {
            Process = process;
            WindowHandle = windowHandle;
            SessionDirectory = sessionDirectory;
            LastKnownBounds = lastKnownBounds;
            LaunchedAtUtc = DateTime.UtcNow;
        }

        public Process? Process { get; }

        public string? SessionDirectory { get; }

        public IntPtr WindowHandle { get; set; }

        public DateTime LaunchedAtUtc { get; }

        public bool CanManageBounds { get; set; } = true;

        public BrowserWindowBounds? LastKnownBounds { get; set; }

        public void Dispose()
        {
            Process?.Dispose();
        }
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
