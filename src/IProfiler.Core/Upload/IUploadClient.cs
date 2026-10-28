namespace IProfiler.Core.Upload;

/// <summary>Uploads an exported file with its index properties to the document repository (M-Files / iVault).</summary>
public interface IUploadClient
{
    Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);
}

public sealed record UploadRequest(string FilePath, string DocumentClass, IReadOnlyDictionary<string, string> Properties);

/// <param name="RemoteId">The repository's id for the new object when <paramref name="Success"/> is true.</param>
public sealed record UploadResult(bool Success, string? RemoteId = null, string? Error = null);
