using System.Windows;
using System.Windows.Threading;
using IProfiler.App.Composition;
using IProfiler.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IProfiler.App;

// Fully qualified: inside IProfiler.*, "Application" resolves to the IProfiler.Application namespace.
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.Default();
        _host = AppHost.Build(paths);
        _host.Start();

        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _logger.LogInformation(
            "iProfiler Desktop {Version} started (install {InstallDirectory}, data {DataDirectory})",
            typeof(App).Assembly.GetName().Version, paths.Install, paths.Data);

        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("iProfiler Desktop exiting with code {ExitCode}", e.ApplicationExitCode);

        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose(); // flushes and closes the Serilog file sink
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        _logger?.LogCritical(e.Exception, "Unhandled exception on the UI thread");

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        _logger?.LogError(e.Exception, "Unobserved task exception");
}
