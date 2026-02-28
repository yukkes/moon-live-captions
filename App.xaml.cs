using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MoonLiveCaptions
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Catch exceptions on the UI thread
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Catch unobserved task exceptions
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Catch exceptions from all threads
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine("DispatcherUnhandledException: " + e.Exception);
            ShowError(e.Exception);
            e.Handled = true; // Prevent app from crashing
        }

        private void OnUnobservedTaskException(object sender,
            System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            Debug.WriteLine("UnobservedTaskException: " + e.Exception);
            e.SetObserved(); // Prevent crash from unobserved task exceptions
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine("DomainUnhandledException: " + e.ExceptionObject);
            // Cannot prevent crash here, but write crash log to disk
            WriteCrashLog(e.ExceptionObject?.ToString() ?? "(no details)");
        }

        private static void WriteCrashLog(string details)
        {
            try
            {
                string logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "crash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(logPath,
                    "MoonLiveCaptions crash log\r\n" +
                    "Time: " + DateTime.Now + "\r\n\r\n" + details,
                    Encoding.UTF8);
            }
            catch { /* best-effort */ }
        }

        private static void ShowError(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    "エラーが発生しました:\n\n" + ex.Message,
                    "MoonLiveCaptions エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* Prevent secondary exception from error display */ }
        }
    }
}
