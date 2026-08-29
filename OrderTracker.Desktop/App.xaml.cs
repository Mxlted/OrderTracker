using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OrderTracker.Desktop.Services;
using OrderTracker.Desktop.ViewModels;

namespace OrderTracker.Desktop;

public partial class App : Application
{
    private const long MaximumCrashLogBytes = 1024 * 1024;
    private const int RetainedCrashLogBytes = 512 * 1024;
    private static readonly object CrashLogLock = new();

    public static string CrashLogPath { get; } = AppPaths.CrashLog;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryWriteCrashLog("Dispatcher exception", e.Exception);

        string? saveError = null;
        var saveFailed = Current.MainWindow?.DataContext is MainViewModel viewModel &&
            !viewModel.TrySaveNow(out saveError);
        var message = $"Order Tracker hit an unexpected error and will keep running.\n\n{e.Exception.Message}\n\nDetails were written to crash.log.";
        if (saveFailed)
        {
            message += $"\n\nSaving also failed: {saveError}";
        }

        e.Handled = true;
        if (Current.MainWindow is Window owner)
        {
            MessageBox.Show(
                owner,
                message,
                "Unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(
                message,
                "Unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryWriteCrashLog("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryWriteCrashLog("AppDomain exception", exception);
        }
        else
        {
            TryWriteCrashLog("AppDomain exception", new InvalidOperationException(e.ExceptionObject?.ToString()));
        }
    }

    private static void TryWriteCrashLog(string source, Exception exception)
    {
        try
        {
            lock (CrashLogLock)
            {
                var folder = Path.GetDirectoryName(CrashLogPath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var entry = $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(CrashLogPath, entry, Encoding.UTF8);
                if (new FileInfo(CrashLogPath).Length > MaximumCrashLogBytes)
                {
                    var contents = File.ReadAllBytes(CrashLogPath);
                    var retainedLength = Math.Min(RetainedCrashLogBytes, contents.Length);
                    File.WriteAllBytes(CrashLogPath, contents[^retainedLength..]);
                }
            }
        }
        catch
        {
        }
    }
}

