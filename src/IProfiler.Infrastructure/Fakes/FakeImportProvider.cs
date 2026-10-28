using System.Runtime.CompilerServices;
using IProfiler.Core.Domain;
using IProfiler.Core.Scanning;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Treats every file as a single page; does not open the files.</summary>
public sealed class FakeImportProvider : IImportProvider
{
    public async IAsyncEnumerable<Page> ImportAsync(
        IReadOnlyList<string> filePaths,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new Page { ImagePath = filePaths[i], Order = i };
        }
    }
}
