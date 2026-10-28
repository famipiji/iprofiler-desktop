using IProfiler.App.Startup;

namespace IProfiler.Tests.App;

public class StartupErrorLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iprofiler-tests", Guid.NewGuid().ToString("N"));
    private readonly Exception _error = new InvalidOperationException("data folder is not writable");

    [Fact]
    public void Writes_to_the_first_candidate_when_it_is_writable()
    {
        var first = Path.Combine(_root, "data", "startup-error.log");

        var written = StartupErrorLog.Write(_error, [first, Path.Combine(_root, "temp", "fallback.log")]);

        Assert.Equal(first, written);
        Assert.Contains("data folder is not writable", File.ReadAllText(first));
    }

    [Fact]
    public void Falls_back_and_records_why_the_first_candidate_failed()
    {
        // A file where the data folder should be makes the first candidate unwritable.
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "data"), "not a folder");
        var first = Path.Combine(_root, "data", "startup-error.log");
        var fallback = Path.Combine(_root, "temp", "fallback.log");

        var written = StartupErrorLog.Write(_error, [first, fallback]);

        Assert.Equal(fallback, written);
        var text = File.ReadAllText(fallback);
        Assert.Contains($"Could not write {first}", text);
        Assert.Contains("data folder is not writable", text);
    }

    [Fact]
    public void Returns_null_when_no_candidate_is_writable()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "blocked"), "not a folder");

        Assert.Null(StartupErrorLog.Write(_error, [Path.Combine(_root, "blocked", "a.log"), Path.Combine(_root, "blocked", "b.log")]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
