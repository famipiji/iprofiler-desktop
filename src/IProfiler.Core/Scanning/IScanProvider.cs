using IProfiler.Core.Domain;

namespace IProfiler.Core.Scanning;

public interface IScanProvider
{
    Task<IReadOnlyList<ScanDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans pages and saves each one as an untouched TIFF under <see cref="AppPaths.Scans"/>.
    /// Pages are yielded as they arrive so the UI can show progress on long ADF runs.
    /// </summary>
    IAsyncEnumerable<Page> ScanAsync(ScanRequest request, CancellationToken cancellationToken = default);
}

public enum ScanDriver
{
    Twain,
    Wia,
}

public sealed record ScanDevice(string Id, string Name, ScanDriver Driver);

public enum ScanColorMode
{
    BlackAndWhite,
    Grayscale,
    Color,
}

/// <param name="ShowDriverUi">False for a silent scan (no vendor dialog).</param>
public sealed record ScanRequest(
    string DeviceId,
    int Dpi = 300,
    ScanColorMode ColorMode = ScanColorMode.BlackAndWhite,
    bool Duplex = false,
    bool ShowDriverUi = false);
