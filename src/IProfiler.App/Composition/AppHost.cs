using System.IO;
using IProfiler.Application.Pages;
using IProfiler.Application.Separation;
using IProfiler.App.ViewModels;
using IProfiler.Core;
using IProfiler.Core.Ai;
using IProfiler.Core.Export;
using IProfiler.Core.HotFolder;
using IProfiler.Core.Imaging;
using IProfiler.Core.Licensing;
using IProfiler.Core.Recognition;
using IProfiler.Core.Scanning;
using IProfiler.Core.Separation;
using IProfiler.Core.Upload;
using IProfiler.Infrastructure.Ai;
using IProfiler.Infrastructure.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace IProfiler.App.Composition;

/// <summary>The composition root: the one place services, providers and logging are wired up.</summary>
public static class AppHost
{
    public const string LogFilePattern = "iprofiler-.log";

    public static IHost Build(AppPaths paths)
    {
        Directory.CreateDirectory(paths.Logs);

        // Empty builder: no appsettings.json, environment variables or content root taken from the
        // current directory (CLAUDE.md rule 6). Settings get their own System.Text.Json file later.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "iProfiler Desktop",
            ContentRootPath = paths.Install,
        });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddSerilog(
            new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(paths.Logs, LogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger(),
            dispose: true);

        builder.Services.AddSingleton(paths);
        AddProviders(builder.Services);
        builder.Services.AddSingleton<ISeparationEngine, SeparationEngine>();
        builder.Services.AddSingleton<IPageAnalyzer, PageAnalyzer>();

        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<MainWindow>();

        return builder.Build();
    }

    // Milestone 0: fakes only. Each is replaced by its real provider in Milestone 1.
    private static void AddProviders(IServiceCollection services)
    {
        services.AddSingleton<IScanProvider, FakeScanProvider>();
        services.AddSingleton<IImportProvider, FakeImportProvider>();
        services.AddSingleton<IOcrProvider, FakeOcrProvider>();
        services.AddSingleton<IBarcodeProvider, FakeBarcodeProvider>();
        services.AddSingleton<IPatchCodeDetector, FakePatchCodeDetector>();
        services.AddSingleton<IImageProcessor, FakeImageProcessor>();
        services.AddSingleton<IPdfAssembler, FakePdfAssembler>();
        services.AddSingleton<ITiffAssembler, FakeTiffAssembler>();
        services.AddSingleton<IUploadClient, FakeUploadClient>();
        services.AddSingleton<IHotFolderWatcher, FakeHotFolderWatcher>();
        services.AddSingleton<ILicenseValidator, FakeLicenseValidator>();
        services.AddSingleton<IAiExtractionProvider, NullAiExtractionProvider>();
    }
}
