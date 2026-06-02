namespace AgentHub.API.Services.KnowledgeBase;

public sealed record KnowledgeBaseOptions(
    Uri BlobContainerUri,
    string? BlobPrefix,
    int ChunkMaxCharacters,
    int ChunkOverlapCharacters,
    int DefaultMaxFiles,
    int MaxChunksPerDocument);