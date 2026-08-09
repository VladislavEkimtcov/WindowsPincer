using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CreditPincher.App.Platform;
using CreditPincher.App.Services;
using CreditPincher.App.Theming;
using CreditPincher.App.Tray;

namespace CreditPincher.App
{
    /// <summary>
    /// Entry point. There is no main window: the app lives in the notification area and
    /// opens windows on demand, which is why <c>ShutdownMode</c> is OnExplicitShutdown.
    /// </summary>
    public partial class App : Application
    {
        private SingleInstance _singleInstance;
        private TrayController _tray;
        private AppServices _services;

        public AppServices Services
        {
            get
            {
                if (_services == null)
                {
                    throw new InvalidOperationException("Services are not available before startup completes.");
                }

                return _services;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstance = new SingleInstance();
            if (!_singleInstance.IsFirstInstance)
            {
                // Hand over to the copy that already owns the tray icon.
                _singleInstance.SignalExistingInstance();
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                LogCrash(args.ExceptionObject as Exception ?? new Exception("Unknown fatal error."));

            try
            {
                _services = new AppServices(Dispatcher, new WpfGitConflictResolver(Dispatcher));

                // Before anything is shown, so no window ever flashes the wrong palette.
                ThemeManager.Apply(_services.Settings.Theme);

                _tray = new TrayController(_services);
            }
            catch (Exception exception)
            {
                LogCrash(exception);
                MessageBox.Show(
                    "CreditPincher could not start:\n\n" + exception.Message,
                    "CreditPincher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            _singleInstance.ShowRequested += () => Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_tray != null)
                {
                    _tray.ShowDashboard();
                }
            }));
            _singleInstance.StartListening();

            var startedByWindows = e.Args.Any(argument =>
                string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase));

            if (!startedByWindows && _services.Settings.ShowDashboardOnStartup)
            {
                _tray.ShowDashboard();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null)
            {
                _tray.Dispose();
            }

            if (_services != null)
            {
                _services.Dispose();
            }

            if (_singleInstance != null)
            {
                _singleInstance.Dispose();
            }

            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception);

            // A tray utility disappearing without a word is worse than an ugly message box.
            MessageBox.Show(
                "Something went wrong:\n\n" + e.Exception.Message + "\n\nCreditPincher will keep running.",
                "CreditPincher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        }

        private static void LogCrash(Exception exception)
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CreditPincher");
                Directory.CreateDirectory(directory);

                File.AppendAllText(
                    Path.Combine(directory, "error.log"),
                    DateTimeOffset.Now.ToString("O") + Environment.NewLine +
                    exception + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception)
            {
                // Logging must never itself take the app down.
            }
        }
    }
}
