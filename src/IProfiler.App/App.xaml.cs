using System.IO;
using System.Windows;
using System.Windows.Threading;
using IProfiler.App.Composition;
using IProfiler.App.Startup;
using IProfiler.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IProfiler.App;

// Fully qualified: inside IProfiler.*, "Application" resolves to the IProfiler.Application namespace.
public partial class App : System.Windows.Application
{
    private const string Title = "iProfiler Desktop";

    private readonly AppPaths _paths = AppPaths.Default();
    private IHost? _host;
    private ILogger<App>? _logger;
    private bool _closingAfterError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = AppHost.Build(_paths);
            _host.Start();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _logger.LogInformation(
                "{Title} {Version} started (install {InstallDirectory}, data {DataDirectory})",
                Title, typeof(App).Assembly.GetName().Version, _paths.Install, _paths.Data);

            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            ReportStartupFailure(ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("{Title} exiting with code {ExitCode}", Title, e.ApplicationExitCode);

        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose(); // flushes and closes the Serilog file sink
        }

        base.OnExit(e);
    }

    private void ReportStartupFailure(Exception ex)
    {
        string details;
        if (_logger is not null)
        {
            _logger.LogCritical(ex, "Startup failed");
            details = $"Details were saved to the log in:\n{_paths.Logs}";
        }
        else
        {
            // Serilog isn't running (it may be what failed), so fall back to a plain file.
            var written = StartupErrorLog.Write(ex, [_paths.StartupErrorLog, Path.Combine(Path.GetTempPath(), "iProfiler-startup-error.log")]);
            details = written is null ? "The details could not be saved." : $"Details were saved to:\n{written}";
        }

        MessageBox.Show($"{Title} could not start.\n\n{ex.Message}\n\n{details}", Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "Unhandled exception on the UI thread; closing");

        // Close in an orderly way (OnExit flushes the log) instead of letting WPF tear the process down.
        e.Handled = true;
        if (_closingAfterError)
            return;

        _closingAfterError = true;
        ShowFriendlyError();
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);

        if (e.IsTerminating && !_closingAfterError)
        {
            _closingAfterError = true;
            ShowFriendlyError();
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        _logger?.LogError(e.Exception, "Unobserved task exception");

    private void ShowFriendlyError() =>
        MessageBox.Show(
            $"Sorry, {Title} ran into an unexpected problem and needs to close.\n\n" +
            $"Details were saved to the log in:\n{_paths.Logs}\n\nPlease send that log to support.",
            Title, MessageBoxButton.OK, MessageBoxImage.Error);
}
