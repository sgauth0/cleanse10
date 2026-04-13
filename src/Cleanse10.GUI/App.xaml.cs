using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Cleanse10.Views;

namespace Cleanse10
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Global unhandled exception handlers — write to a crash log next to the exe
            // so silent crashes become diagnosable.
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
                WriteCrashLog("AppDomain", ex.ExceptionObject as Exception);

            DispatcherUnhandledException += (_, ex) =>
            {
                // Known WPF internal bug: Popup.CreateRootPopupInternal creates a
                // two-way binding without a Path when opening ToolTip popups.
                // Nothing we can do about it — just swallow and move on.
                string msg = ex.Exception.Message;
                if (msg.Contains("Two-way binding requires Path", StringComparison.Ordinal)
                    || msg.Contains("TwoWay or OneWayToSource binding cannot work on the read-only property", StringComparison.Ordinal))
                {
                    ex.Handled = true;
                    return;
                }

                WriteCrashLog("Dispatcher", ex.Exception);
                ex.Handled = true;   // prevent silent exit; show message instead
                MessageBox.Show(
                    ex.Exception.Message,
                    "Cleanse10 — Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };

            base.OnStartup(e);

            try
            {
                var window = new MainWindow();
                window.Show();
            }
            catch (Exception ex)
            {
                WriteCrashLog("OnStartup", ex);
                MessageBox.Show(
                    $"Failed to start:\n\n{ex}",
                    "Cleanse10 — Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }

        private static void WriteCrashLog(string source, Exception? ex)
        {
            try
            {
                string logPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "cleanse10-crash.log");
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:u}] [{source}]\n{ex}\n\n");
            }
            catch { /* nowhere left to report */ }
        }
    }
}
