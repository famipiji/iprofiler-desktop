using BitMiracle.LibTiff.Classic;

namespace IProfiler.Infrastructure.Imaging;

/// <summary>
/// LibTiff reports problems through a process-wide handler that by default writes to the console. This one
/// keeps the last error per thread so callers can put it in their exception or log message.
/// </summary>
internal sealed class TiffMessages : TiffErrorHandler
{
    [ThreadStatic]
    private static string? _lastError;

    static TiffMessages() => Tiff.SetErrorHandler(new TiffMessages());

    /// <summary>The last error LibTiff reported on this thread, if any.</summary>
    public static string? LastError => _lastError;

    /// <summary>Installs the handler (once); call before any LibTiff use.</summary>
    public static void Install() => _lastError = null;

    public override void ErrorHandler(Tiff tif, string method, string format, params object[] args) =>
        _lastError = $"{method}: {string.Format(format, args)}";

    public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
    {
        // Same message as ErrorHandler; recorded there.
    }

    // Scanner software writes private tags LibTiff doesn't know; those warnings are harmless and frequent.
    public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
    {
    }

    public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
    {
    }
}
