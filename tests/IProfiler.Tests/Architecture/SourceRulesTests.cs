using System.Text.RegularExpressions;

namespace IProfiler.Tests.Architecture;

/// <summary>Source-level checks for CLAUDE.md rules that compiled-IL tests can't see.</summary>
public partial class SourceRulesTests
{
    [Fact]
    public void No_source_uses_the_current_directory()
    {
        // Rule 6: paths come from AppPaths only.
        var offenders = SourceFiles()
            .SelectMany(file => CodeLines(file)
                .Where(line => CurrentDirectory().IsMatch(line.Text))
                .Select(line => $"{Relative(file)}:{line.Number}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Use AppPaths instead of the current directory: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_source_has_an_empty_catch()
    {
        // Rule 5: every catch logs (or rethrows).
        var offenders = SourceFiles()
            .Where(file => EmptyCatch().IsMatch(File.ReadAllText(file)))
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "Empty catch blocks (log or rethrow): " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_package_downloads_models_at_runtime()
    {
        // Rule 8 (offline): models ship as files under AppPaths.Models. Sdcb's "Online" model packages
        // download from the internet on first use, so they must never be referenced.
        var props = File.ReadAllText(Path.Combine(RepoRoot.Path, "Directory.Packages.props"));

        Assert.DoesNotMatch(@"Include=""[^""]*\.Models\.Online", props);
    }

    [GeneratedRegex(@"Environment\.CurrentDirectory|Directory\.(Get|Set)CurrentDirectory")]
    private static partial Regex CurrentDirectory();

    // "catch", optional "(...)" and "when (...)", then a body that is empty or holds only // comments.
    [GeneratedRegex(@"\bcatch\b[^{;]*\{\s*(//[^\n]*\s*)*\}")]
    private static partial Regex EmptyCatch();

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // Lines that aren't comments, so docs may still say "never use Environment.CurrentDirectory".
    private static IEnumerable<(int Number, string Text)> CodeLines(string file) =>
        File.ReadLines(file)
            .Select((text, i) => (Number: i + 1, Text: text))
            .Where(line => !line.Text.TrimStart().StartsWith("//", StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot.Path, file);
}
