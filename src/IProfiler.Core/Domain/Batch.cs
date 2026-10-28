namespace IProfiler.Core.Domain;

/// <summary>Top level of the explorer tree (D4): Batch → Document → Page.</summary>
public sealed class Batch
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public List<Document> Documents { get; init; } = [];
}
