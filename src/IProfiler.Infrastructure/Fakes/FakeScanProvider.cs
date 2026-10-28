using System.Runtime.CompilerServices;
using IProfiler.Core;
using IProfiler.Core.Domain;
using IProfiler.Core.Scanning;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Returns the <see cref="FakeBatch"/> pages. Writes no files; the image paths are placeholders.</summary>
public sealed class FakeScanProvider(AppPaths paths, ILogger<FakeScanProvider> logger) : IScanProvider
{
    public const string DeviceId = "fake-scanner";

    public Task<IReadOnlyList<ScanDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ScanDevice>>([new ScanDevice(DeviceId, "Fake Scanner (TWAIN)", ScanDriver.Twain)]);

    public async IAsyncEnumerable<Page> ScanAsync(
        ScanRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fake scan on {DeviceId} at {Dpi} dpi", request.DeviceId, request.Dpi);
        var folder = Path.Combine(paths.Scans, "fake");

        for (var i = 0; i < FakeBatch.Pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            var fake = FakeBatch.Pages[i];
            yield return new Page
            {
                ImagePath = Path.Combine(folder, fake.FileName),
                Order = i,
                PatchCode = fake.PatchCode,
            };
        }
    }
}
