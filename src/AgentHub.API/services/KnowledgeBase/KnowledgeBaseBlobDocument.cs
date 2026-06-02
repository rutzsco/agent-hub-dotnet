namespace AgentHub.API.Services.KnowledgeBase;

public sealed record KnowledgeBaseBlobDocument(
    string BlobPath,
    string FileName,
    string? ContentType,
    long? SizeBytes,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? LastModified,
    string? MetadataCategory,
    string? MetadataSubcategory,
    string? MetadataDocumentType,
    BinaryData Content)
{
    public string ParentId => SemanticChunker.CreateParentId(BlobPath);
}