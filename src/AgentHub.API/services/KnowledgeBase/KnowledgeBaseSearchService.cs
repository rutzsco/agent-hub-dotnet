namespace AgentHub.API.Services.KnowledgeBase;

public sealed class KnowledgeBaseSearchService
{
    private readonly KnowledgeBaseEmbeddingService _embeddingService;
    private readonly IKnowledgeBaseRepository _repository;

    public KnowledgeBaseSearchService(
        KnowledgeBaseEmbeddingService embeddingService,
        IKnowledgeBaseRepository repository)
    {
        _embeddingService = embeddingService;
        _repository = repository;
    }

    public async Task<IReadOnlyList<KnowledgeBaseSearchHit>> SearchAsync(
        string query,
        int topK,
        KnowledgeBaseSearchFilter? filter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<KnowledgeBaseSearchHit>();
        }

        var vector = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        return await _repository.SearchAsync(vector, Math.Clamp(topK, 1, 50), filter, cancellationToken);
    }
}