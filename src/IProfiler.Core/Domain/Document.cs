namespace IProfiler.Core.Domain;

public sealed class Document
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Position of the document within its batch (0-based).</summary>
    public int Order { get; set; }

    public List<Page> Pages { get; init; } = [];
}
