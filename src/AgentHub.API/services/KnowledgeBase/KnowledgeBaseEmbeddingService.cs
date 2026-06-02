using OpenAI.Embeddings;

namespace AgentHub.API.Services.KnowledgeBase;

public sealed class KnowledgeBaseEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;

    public KnowledgeBaseEmbeddingService(EmbeddingClient embeddingClient)
    {
        _embeddingClient = embeddingClient;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var response = await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        return response.Value.ToFloats().ToArray();
    }
}