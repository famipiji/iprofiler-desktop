using System.Runtime.CompilerServices;
using IProfiler.Core.HotFolder;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Never reports a file; completes immediately.</summary>
public sealed class FakeHotFolderWatcher : IHotFolderWatcher
{
    public async IAsyncEnumerable<string> WatchAsync(
        string folderPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
