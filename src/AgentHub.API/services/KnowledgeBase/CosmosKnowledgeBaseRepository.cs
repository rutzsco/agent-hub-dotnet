using System.Collections.ObjectModel;
using AgentHub.API.services.conversations;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace AgentHub.API.Services.KnowledgeBase;

public sealed class CosmosKnowledgeBaseRepository : CosmosRepositoryBase, IKnowledgeBaseRepository
{
    private const string ContentVectorPath = "/content_vector";
    private const int EmbeddingDimensions = 1536;

    public CosmosKnowledgeBaseRepository(
        CosmosOptions options,
        ILogger<CosmosKnowledgeBaseRepository> logger)
        : base(options, options.KnowledgeBaseContainerName, "/parent_id", logger)
    {
    }

    public async Task<KnowledgeBaseDocumentIndexState?> GetDocumentIndexStateAsync(
        string parentId,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);
        var partitionKey = new PartitionKey(parentId);
        var query = new QueryDefinition("""
            SELECT VALUE {
                "parentId": @parentId,
                "blobLastModified": MAX(c.blob_last_modified),
                "lastIndexed": MAX(c.last_indexed),
                "chunkCount": COUNT(1)
            }
            FROM c
            WHERE c.parent_id = @parentId
            """)
            .WithParameter("@parentId", parentId);

        using var feed = container.GetItemQueryIterator<KnowledgeBaseDocumentIndexStateDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellationToken);
            var doc = page.FirstOrDefault();
            if (doc is null || doc.chunkCount <= 0)
            {
                return null;
            }

            return new KnowledgeBaseDocumentIndexState(
                parentId,
                doc.blobLastModified,
                doc.lastIndexed,
                doc.chunkCount);
        }

        return null;
    }

    public async Task ReplaceDocumentChunksAsync(
        string parentId,
        IReadOnlyList<KnowledgeBaseChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);
        var partitionKey = new PartitionKey(parentId);

        await DeleteExistingChunksAsync(container, parentId, partitionKey, cancellationToken);

        foreach (var chunk in chunks)
        {
            var document = KnowledgeBaseChunkDocument.From(chunk);
            await container.UpsertItemAsync(document, partitionKey, cancellationToken: cancellationToken);
        }

        Logger.LogInformation("Indexed {ChunkCount} KnowledgeBase chunks. ParentId={ParentId}", chunks.Count, parentId);
    }

    public async Task<IReadOnlyList<KnowledgeBaseSearchHit>> SearchAsync(
        IReadOnlyList<float> queryVector,
        int topK,
        KnowledgeBaseSearchFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);
        var query = BuildSearchQuery(queryVector, topK, filter);
        var hits = new List<KnowledgeBaseSearchHit>(capacity: topK);

        using var feed = container.GetItemQueryIterator<KnowledgeBaseSearchDocument>(query);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellationToken);
            foreach (var doc in page)
            {
                hits.Add(new KnowledgeBaseSearchHit(doc.ToChunk(), doc.score));
            }
        }

        return hits;
    }

    protected override ContainerProperties CreateContainerProperties()
    {
        var properties = new ContainerProperties(ContainerName, PartitionKeyPath)
        {
            VectorEmbeddingPolicy = new VectorEmbeddingPolicy(new Collection<Embedding>
            {
                new()
                {
                    Path = ContentVectorPath,
                    DataType = VectorDataType.Float32,
                    Dimensions = EmbeddingDimensions,
                    DistanceFunction = DistanceFunction.Cosine
                }
            })
        };

        properties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
        properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/\"_etag\"/?" });
        properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/content_vector/*" });
        properties.IndexingPolicy.VectorIndexes.Add(new VectorIndexPath
        {
            Path = ContentVectorPath,
            Type = VectorIndexType.QuantizedFlat
        });

        return properties;
    }

    private static async Task DeleteExistingChunksAsync(
        Container container,
        string parentId,
        PartitionKey partitionKey,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT c.id FROM c WHERE c.parent_id = @parentId")
            .WithParameter("@parentId", parentId);

        using var feed = container.GetItemQueryIterator<KnowledgeBaseChunkIdDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellationToken);
            foreach (var doc in page)
            {
                await container.DeleteItemAsync<KnowledgeBaseChunkIdDocument>(doc.id, partitionKey, cancellationToken: cancellationToken);
            }
        }
    }

    private static QueryDefinition BuildSearchQuery(
        IReadOnlyList<float> queryVector,
        int topK,
        KnowledgeBaseSearchFilter? filter)
    {
        var clauses = new List<string>();
        var query = new QueryDefinition($"""
            SELECT TOP {Math.Clamp(topK, 1, 50)}
                c.id,
                c.chunk_id,
                c.parent_id,
                c.chunk_index,
                c.content,
                c.title,
                c.blob_path,
                c.file_name,
                c.content_type,
                c.size_bytes,
                c.created_on,
                c.blob_last_modified,
                c.metadata_category,
                c.metadata_subcategory,
                c.metadata_document_type,
                c.page_number,
                c.last_indexed,
                c.content_vector,
                1 - VectorDistance(c.content_vector, @queryVector) AS score
            FROM c
            {BuildWhereClause(filter, clauses)}
            ORDER BY VectorDistance(c.content_vector, @queryVector)
            """)
            .WithParameter("@queryVector", queryVector.ToArray());

        AddFilterParameters(query, filter);
        return query;
    }

    private static string BuildWhereClause(KnowledgeBaseSearchFilter? filter, List<string> clauses)
    {
        if (filter is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(filter.Category)) clauses.Add("c.metadata_category = @category");
        if (!string.IsNullOrWhiteSpace(filter.Subcategory)) clauses.Add("c.metadata_subcategory = @subcategory");
        if (!string.IsNullOrWhiteSpace(filter.DocumentType)) clauses.Add("c.metadata_document_type = @documentType");
        if (!string.IsNullOrWhiteSpace(filter.BlobPath)) clauses.Add("c.blob_path = @blobPath");
        if (!string.IsNullOrWhiteSpace(filter.BlobPrefix)) clauses.Add("STARTSWITH(c.blob_path, @blobPrefix)");
        if (!string.IsNullOrWhiteSpace(filter.FileName)) clauses.Add("c.file_name = @fileName");

        return clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
    }

    private static void AddFilterParameters(QueryDefinition query, KnowledgeBaseSearchFilter? filter)
    {
        if (filter is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(filter.Category)) query.WithParameter("@category", filter.Category);
        if (!string.IsNullOrWhiteSpace(filter.Subcategory)) query.WithParameter("@subcategory", filter.Subcategory);
        if (!string.IsNullOrWhiteSpace(filter.DocumentType)) query.WithParameter("@documentType", filter.DocumentType);
        if (!string.IsNullOrWhiteSpace(filter.BlobPath)) query.WithParameter("@blobPath", filter.BlobPath);
        if (!string.IsNullOrWhiteSpace(filter.BlobPrefix)) query.WithParameter("@blobPrefix", filter.BlobPrefix);
        if (!string.IsNullOrWhiteSpace(filter.FileName)) query.WithParameter("@fileName", filter.FileName);
    }

    private sealed record KnowledgeBaseChunkIdDocument(string id);

    private sealed record KnowledgeBaseDocumentIndexStateDocument(
        string parentId,
        DateTimeOffset? blobLastModified,
        DateTimeOffset lastIndexed,
        int chunkCount);

    private sealed record KnowledgeBaseChunkDocument(
        [property: JsonProperty("id")] string id,
        [property: JsonProperty("chunk_id")] string chunk_id,
        [property: JsonProperty("parent_id")] string parent_id,
        [property: JsonProperty("chunk_index")] int chunk_index,
        [property: JsonProperty("content")] string content,
        [property: JsonProperty("title")] string title,
        [property: JsonProperty("blob_path")] string blob_path,
        [property: JsonProperty("file_name")] string file_name,
        [property: JsonProperty("content_type")] string? content_type,
        [property: JsonProperty("size_bytes")] long? size_bytes,
        [property: JsonProperty("created_on")] DateTimeOffset? created_on,
        [property: JsonProperty("blob_last_modified")] DateTimeOffset? blob_last_modified,
        [property: JsonProperty("metadata_category")] string? metadata_category,
        [property: JsonProperty("metadata_subcategory")] string? metadata_subcategory,
        [property: JsonProperty("metadata_document_type")] string? metadata_document_type,
        [property: JsonProperty("page_number")] int? page_number,
        [property: JsonProperty("last_indexed")] DateTimeOffset last_indexed,
        [property: JsonProperty("content_vector")] IReadOnlyList<float> content_vector)
    {
        public static KnowledgeBaseChunkDocument From(KnowledgeBaseChunk chunk)
        {
            return new KnowledgeBaseChunkDocument(
                chunk.ChunkId,
                chunk.ChunkId,
                chunk.ParentId,
                chunk.ChunkIndex,
                chunk.Content,
                chunk.Title,
                chunk.BlobPath,
                chunk.FileName,
                chunk.ContentType,
                chunk.SizeBytes,
                chunk.CreatedOn,
                chunk.BlobLastModified,
                chunk.MetadataCategory,
                chunk.MetadataSubcategory,
                chunk.MetadataDocumentType,
                chunk.PageNumber,
                chunk.LastIndexed,
                chunk.ContentVector);
        }
    }

    private sealed record KnowledgeBaseSearchDocument(
        string id,
        string chunk_id,
        string parent_id,
        int chunk_index,
        string content,
        string title,
        string blob_path,
        string file_name,
        string? content_type,
        long? size_bytes,
        DateTimeOffset? created_on,
        DateTimeOffset? blob_last_modified,
        string? metadata_category,
        string? metadata_subcategory,
        string? metadata_document_type,
        int? page_number,
        DateTimeOffset last_indexed,
        IReadOnlyList<float> content_vector,
        double score)
    {
        public KnowledgeBaseChunk ToChunk()
        {
            return new KnowledgeBaseChunk(
                chunk_id,
                parent_id,
                chunk_index,
                content,
                title,
                blob_path,
                file_name,
                content_type,
                size_bytes,
                created_on,
                blob_last_modified,
                metadata_category,
                metadata_subcategory,
                metadata_document_type,
                page_number,
                last_indexed,
                content_vector);
        }
    }
}