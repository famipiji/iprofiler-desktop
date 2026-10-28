using IProfiler.Core.Domain;
using IProfiler.Core.Recognition;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Returns each page's canned patch code.</summary>
public sealed class FakePatchCodeDetector : IPatchCodeDetector
{
    public Task<PatchCode?> DetectAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(FakeBatch.Find(imagePath)?.PatchCode);
}
