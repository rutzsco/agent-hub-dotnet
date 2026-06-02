using Newtonsoft.Json;

namespace AgentHub.API.Services.KnowledgeBase;

public sealed record KnowledgeBaseChunk(
    [property: JsonProperty("chunk_id")] string ChunkId,
    [property: JsonProperty("parent_id")] string ParentId,
    [property: JsonProperty("chunk_index")] int ChunkIndex,
    [property: JsonProperty("content")] string Content,
    [property: JsonProperty("title")] string Title,
    [property: JsonProperty("blob_path")] string BlobPath,
    [property: JsonProperty("file_name")] string FileName,
    [property: JsonProperty("content_type")] string? ContentType,
    [property: JsonProperty("size_bytes")] long? SizeBytes,
    [property: JsonProperty("created_on")] DateTimeOffset? CreatedOn,
    [property: JsonProperty("blob_last_modified")] DateTimeOffset? BlobLastModified,
    [property: JsonProperty("metadata_category")] string? MetadataCategory,
    [property: JsonProperty("metadata_subcategory")] string? MetadataSubcategory,
    [property: JsonProperty("metadata_document_type")] string? MetadataDocumentType,
    [property: JsonProperty("page_number")] int? PageNumber,
    [property: JsonProperty("last_indexed")] DateTimeOffset LastIndexed,
    [property: JsonProperty("content_vector")] IReadOnlyList<float> ContentVector);

public sealed record KnowledgeBaseSearchHit(
    KnowledgeBaseChunk Chunk,
    double Score);