using System;
using System.IO;

namespace OrderTracker.Desktop.Services;

internal static class AppPaths
{
    public static string RootFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OrderTrackerDesktop");

    public static string DataFile { get; } = Path.Combine(RootFolder, "orders.json");

    public static string IconCacheFolder { get; } = Path.Combine(RootFolder, "merchant-icons");

    public static string BrowserSessionsFolder { get; } = Path.Combine(RootFolder, "browser-sessions");

    public static string CrashLog { get; } = Path.Combine(RootFolder, "crash.log");
}
