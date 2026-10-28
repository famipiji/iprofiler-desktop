using IProfiler.Core.Domain;

namespace IProfiler.Core.Separation;

/// <summary>
/// The single separation entry point used by every scan, import and hot-folder path
/// (CLAUDE.md rule 4). The mode comes from <see cref="SeparationSettings.Mode"/>.
/// </summary>
public interface ISeparationEngine
{
    Task<SeparationResult> SeparateAsync(
        IReadOnlyList<Page> pages,
        SeparationSettings settings,
        CancellationToken cancellationToken = default);
}

/// <param name="Documents">The documents, in input order. Never contains an empty document.</param>
/// <param name="RemovedSeparators">Separator pages that were dropped from the output.</param>
public sealed record SeparationResult(IReadOnlyList<Document> Documents, IReadOnlyList<Page> RemovedSeparators);
