namespace TenderHack.Domain.ValueObjects;

public sealed record SourceRef(Guid ChunkId, string DocumentTitle, string Excerpt, double Score);
