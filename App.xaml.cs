using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MoonLiveCaptions.Helpers;

namespace MoonLiveCaptions
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure %LOCALAPPDATA%\MoonLiveCaptions\ directories exist and load settings
            AppSettings.EnsureDirectories();
            AppSettings.Load();

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
                AppSettings.EnsureDirectories();
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MoonLiveCaptions",
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
                // Use language-appropriate message if settings already loaded
                bool isJa = AppSettings.UILanguage != "en";
                string title = isJa ? "MoonLiveCaptions エラー" : "MoonLiveCaptions Error";
                string body  = isJa
                    ? "エラーが発生しました:\n\n" + ex.Message
                    : "An error occurred:\n\n" + ex.Message;
                MessageBox.Show(
                    body,
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* Prevent secondary exception from error display */ }
        }
    }
}
