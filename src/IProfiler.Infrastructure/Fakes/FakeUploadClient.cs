using IProfiler.Core.Upload;
using Microsoft.Extensions.Logging;

namespace IProfiler.Infrastructure.Fakes;

/// <summary>Uploads nothing; reports success with a made-up id.</summary>
public sealed class FakeUploadClient(ILogger<FakeUploadClient> logger) : IUploadClient
{
    public Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Fake upload of {FilePath} as {DocumentClass}", request.FilePath, request.DocumentClass);
        return Task.FromResult(new UploadResult(true, RemoteId: $"fake-{Guid.NewGuid():N}"));
    }
}
