namespace IProfiler.Tests;

/// <summary>The repository root, found by walking up from the test output folder to IProfiler.Desktop.sln.</summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> Root = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "IProfiler.Desktop.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("IProfiler.Desktop.sln not found above the test output folder.");
    });

    public static string Path => Root.Value;
}
