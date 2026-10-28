using System.IO;

namespace IProfiler.App.Startup;

/// <summary>
/// Last-resort log for failures before Serilog is running (e.g. the data folder is not writable).
/// Tries each candidate file in turn; a later file also records why the earlier ones failed.
/// </summary>
public static class StartupErrorLog
{
    /// <returns>The file the entry was written to, or null if none of the candidates were writable.</returns>
    public static string? Write(Exception error, IEnumerable<string> candidatePaths)
    {
        var failures = new List<string>();

        foreach (var path in candidatePaths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, Entry(error, failures));
                return path;
            }
            catch (Exception ex)
            {
                // Logging isn't up yet, so the failure is carried into the next candidate's entry.
                failures.Add($"Could not write {path}: {ex.Message}");
            }
        }

        return null;
    }

    private static string Entry(Exception error, IReadOnlyList<string> failures)
    {
        var version = typeof(StartupErrorLog).Assembly.GetName().Version;
        var lines = new List<string> { $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} iProfiler Desktop {version} failed to start" };
        lines.AddRange(failures);
        lines.Add(error.ToString());
        lines.Add(string.Empty);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
