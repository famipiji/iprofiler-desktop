namespace IProfiler.Core;

/// <summary>
/// The only place file-system roots come from (CLAUDE.md rule 6).
/// Install files (models, native libs) live next to the executable; data lives under
/// %LOCALAPPDATA%\iProfiler\. Never use Environment.CurrentDirectory: a Start-menu
/// shortcut or a hot-folder launch can start the app with any working directory.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string installDirectory, string dataDirectory)
    {
        Install = Path.GetFullPath(installDirectory);
        Data = Path.GetFullPath(dataDirectory);
    }

    /// <summary>Paths for the installed app: <see cref="AppContext.BaseDirectory"/> and %LOCALAPPDATA%\iProfiler\.</summary>
    public static AppPaths Default() => new(
        AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iProfiler"));

    /// <summary>Install directory (read-only at runtime).</summary>
    public string Install { get; }

    /// <summary>Per-user data directory (SQLite, working TIFFs, logs).</summary>
    public string Data { get; }

    public string Models => Path.Combine(Install, "models");

    public string Logs => Path.Combine(Data, "logs");

    public string Scans => Path.Combine(Data, "scans");

    public string Database => Path.Combine(Data, "iprofiler.db");
}
