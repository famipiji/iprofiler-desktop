using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace IProfiler.Tests.Architecture;

/// <summary>
/// Enforces the layering rules in CLAUDE.md ("Architecture rules (review-blocking)").
/// Never weaken or skip these; fix the code instead.
/// </summary>
public class LayeringTests
{
    private const string Core = "IProfiler.Core";
    private const string Application = "IProfiler.Application";
    private const string Infrastructure = "IProfiler.Infrastructure";
    private const string Data = "IProfiler.Data";
    private const string App = "IProfiler.App";

    private const string LoggingAbstractions = "Microsoft.Extensions.Logging.Abstractions";

    // Project -> projects it may reference. Every project under src/ must be listed here.
    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
    {
        [Core] = [],
        [Application] = [Core],
        [Infrastructure] = [Core],
        [Data] = [Core],
        [App] = [Core, Application, Infrastructure, Data],
    };

    // Rule 1: third-party imaging/scan/OCR/PDF libraries.
    private static readonly string[] ImagingLibraries =
        ["NTwain", "NAPS2", "Sdcb", "OpenCvSharp", "ZXing", "PdfSharp", "QuestPDF", "BitMiracle", "Tesseract"];

    /// <summary>A group of libraries only one project may use, checked in compiled IL and in PackageReferences.</summary>
    private sealed record Restriction(string Name, string AllowedIn, string[] Namespaces, string[] PackagePrefixes);

    private static readonly Restriction[] Restrictions =
    [
        new("imaging", Infrastructure, ImagingLibraries, ImagingLibraries),
        // Storage only lives in Data; the App wires it up through an extension method that Data exposes.
        new("efcore", Data, ["Microsoft.EntityFrameworkCore"], ["Microsoft.EntityFrameworkCore"]),
        // Serilog is configured in the composition root only; every other layer logs through ILogger<T>.
        new("serilog", App, ["Serilog"], ["Serilog"]),
    ];

    public static TheoryData<string, string> RestrictedProjects()
    {
        var data = new TheoryData<string, string>();
        foreach (var restriction in Restrictions)
        {
            foreach (var project in AllowedProjectReferences.Keys.Where(p => p != restriction.AllowedIn))
                data.Add(restriction.Name, project);
        }

        return data;
    }

    public static TheoryData<string> AllProjects => new(AllowedProjectReferences.Keys);

    public static TheoryData<string> AbstractLoggingProjects => new(Core, Application, Infrastructure);

    [Theory]
    [MemberData(nameof(RestrictedProjects))]
    public void Types_do_not_depend_on_libraries_restricted_to_another_project(string restriction, string project)
    {
        var rule = Restrictions.Single(r => r.Name == restriction);

        var result = Types.InAssembly(Assembly.Load(project))
            .ShouldNot()
            .HaveDependencyOnAny(rule.Namespaces)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"{project} uses {restriction} types, which belong in {rule.AllowedIn}: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(RestrictedProjects))]
    public void Projects_have_no_package_references_restricted_to_another_project(string restriction, string project)
    {
        var rule = Restrictions.Single(r => r.Name == restriction);

        var restricted = PackageReferences(project)
            .Where(p => rule.PackagePrefixes.Any(r => p.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(restricted.Count == 0,
            $"{project} references {string.Join(", ", restricted)}; only {rule.AllowedIn} may.");
    }

    [Theory]
    [MemberData(nameof(AbstractLoggingProjects))]
    public void Project_logs_only_through_logging_abstractions(string project)
    {
        var loggingPackages = PackageReferences(project)
            .Where(p => p.StartsWith("Microsoft.Extensions.Logging", StringComparison.OrdinalIgnoreCase) ||
                        p.StartsWith("Serilog", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Equals(LoggingAbstractions, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(loggingPackages.Count == 0,
            $"{project} references {string.Join(", ", loggingPackages)}; use ILogger<T> from {LoggingAbstractions}.");
    }

    [Fact]
    public void Core_has_no_package_references_except_logging_abstractions()
    {
        var packages = PackageReferences(Core)
            .Where(p => !p.Equals(LoggingAbstractions, StringComparison.OrdinalIgnoreCase));

        Assert.Empty(packages);
    }

    [Theory]
    [MemberData(nameof(AllProjects))]
    public void Project_references_follow_the_layering(string project)
    {
        var references = LoadProject(project)
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension((string)r.Attribute("Include")!));

        var notAllowed = references.Except(AllowedProjectReferences[project]).ToList();

        Assert.True(notAllowed.Count == 0,
            $"{project} must not reference {string.Join(", ", notAllowed)}.");
    }

    [Fact]
    public void Every_source_project_is_covered_by_these_rules()
    {
        var projects = Directory.GetFiles(Path.Combine(RepoRoot.Path, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Order();

        Assert.Equal(AllowedProjectReferences.Keys.Order(), projects);
    }

    private static IEnumerable<string> PackageReferences(string project) =>
        LoadProject(project).Descendants("PackageReference").Select(r => (string)r.Attribute("Include")!);

    private static XDocument LoadProject(string project) =>
        XDocument.Load(Path.Combine(RepoRoot.Path, "src", project, $"{project}.csproj"));
}
