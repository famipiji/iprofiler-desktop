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
    // Rule 1: third-party imaging/scan/OCR/PDF libraries may only be used in IProfiler.Infrastructure.
    private static readonly string[] RestrictedLibraries =
        ["NTwain", "NAPS2", "Sdcb", "OpenCvSharp", "ZXing", "PdfSharp", "QuestPDF", "BitMiracle", "Tesseract"];

    private const string Infrastructure = "IProfiler.Infrastructure";

    // Project -> projects it may reference. Every project under src/ must be listed here.
    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
    {
        ["IProfiler.Core"] = [],
        ["IProfiler.Application"] = ["IProfiler.Core"],
        ["IProfiler.Infrastructure"] = ["IProfiler.Core"],
        ["IProfiler.Data"] = ["IProfiler.Core"],
        ["IProfiler.App"] = ["IProfiler.Core", "IProfiler.Application", "IProfiler.Infrastructure", "IProfiler.Data"],
    };

    public static TheoryData<string> ProjectsOutsideInfrastructure =>
        new(AllowedProjectReferences.Keys.Where(p => p != Infrastructure));

    public static TheoryData<string> AllProjects => new(AllowedProjectReferences.Keys);

    [Theory]
    [MemberData(nameof(ProjectsOutsideInfrastructure))]
    public void Types_outside_infrastructure_do_not_depend_on_restricted_libraries(string project)
    {
        var result = Types.InAssembly(Assembly.Load(project))
            .ShouldNot()
            .HaveDependencyOnAny(RestrictedLibraries)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"{project} uses a library that belongs in {Infrastructure}: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(ProjectsOutsideInfrastructure))]
    public void Projects_outside_infrastructure_have_no_restricted_package_references(string project)
    {
        var restricted = PackageReferences(project)
            .Where(p => RestrictedLibraries.Any(r => p.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(restricted.Count == 0,
            $"{project} references {string.Join(", ", restricted)}; only {Infrastructure} may.");
    }

    [Fact]
    public void Core_has_no_package_references()
    {
        Assert.Empty(PackageReferences("IProfiler.Core"));
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
        var projects = Directory.GetFiles(Path.Combine(RepoRoot.Value, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Order();

        Assert.Equal(AllowedProjectReferences.Keys.Order(), projects);
    }

    private static IEnumerable<string> PackageReferences(string project) =>
        LoadProject(project).Descendants("PackageReference").Select(r => (string)r.Attribute("Include")!);

    private static XDocument LoadProject(string project) =>
        XDocument.Load(Path.Combine(RepoRoot.Value, "src", project, $"{project}.csproj"));

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IProfiler.Desktop.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("IProfiler.Desktop.sln not found above the test output folder.");
    });
}
