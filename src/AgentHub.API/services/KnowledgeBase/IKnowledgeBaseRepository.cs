namespace AgentHub.API.Services.KnowledgeBase;

public interface IKnowledgeBaseRepository
{
    Task<KnowledgeBaseDocumentIndexState?> GetDocumentIndexStateAsync(
        string parentId,
        CancellationToken cancellationToken = default);

    Task ReplaceDocumentChunksAsync(
        string parentId,
        IReadOnlyList<KnowledgeBaseChunk> chunks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeBaseSearchHit>> SearchAsync(
        IReadOnlyList<float> queryVector,
        int topK,
        KnowledgeBaseSearchFilter? filter = null,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeBaseDocumentIndexState(
    string ParentId,
    DateTimeOffset? BlobLastModified,
    DateTimeOffset LastIndexed,
    int ChunkCount)
{
    public bool IsCurrentFor(KnowledgeBaseBlobDocument document)
    {
        if (ChunkCount <= 0 || BlobLastModified is null || document.LastModified is null)
        {
            return false;
        }

        return BlobLastModified >= document.LastModified;
    }
}

public sealed record KnowledgeBaseSearchFilter(
    string? Category = null,
    string? Subcategory = null,
    string? DocumentType = null,
    string? BlobPath = null,
    string? BlobPrefix = null,
    string? FileName = null);