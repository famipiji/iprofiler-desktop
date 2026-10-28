using IProfiler.App.Composition;
using IProfiler.App.ViewModels;
using IProfiler.Core;
using IProfiler.Infrastructure.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace IProfiler.Tests.App;

public class AppHostTests : IDisposable
{
    private readonly AppPaths _paths = new(
        AppContext.BaseDirectory,
        Path.Combine(Path.GetTempPath(), "iprofiler-tests", Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Host_wires_the_fake_providers_and_writes_the_log_file()
    {
        // Build validates every registration (ValidateOnBuild), including MainWindow's constructor.
        using (var host = AppHost.Build(_paths))
        {
            await host.StartAsync();

            var viewModel = host.Services.GetRequiredService<MainViewModel>();
            await viewModel.LoadAsync();
            Assert.Equal(FakeBatch.Pages.Count, viewModel.Pages.Count);

            await host.StopAsync();
        }

        var logFile = Assert.Single(Directory.GetFiles(_paths.Logs, "iprofiler-*.log"));
        Assert.Contains($"Loaded {FakeBatch.Pages.Count} pages", File.ReadAllText(logFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_paths.Data))
            Directory.Delete(_paths.Data, recursive: true);
    }
}
