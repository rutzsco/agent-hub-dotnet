using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using OpenAI.Embeddings;

namespace AgentHub.API.services.search;

/// <summary>
/// Hybrid retriever over the <see cref="LeanSearchIndex"/> for Lean/Kaizen artifacts.
/// Combines BM25 keyword search on <c>content</c> with vector search on <c>contentVector</c>,
/// optionally filtered by Lean/org metadata (artifact type, value stream, site).
/// </summary>
/// <remarks>
/// Stateless and safe to register as a singleton. Embeddings are generated per query using the
/// configured Azure OpenAI deployment (<see cref="Settings.MemoryEmbeddingModel"/>).
/// </remarks>
public sealed class LeanSearchRetriever
{
    private readonly SearchClient _searchClient;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<LeanSearchRetriever> _logger;

    /// <param name="searchEndpoint">Azure AI Search service endpoint.</param>
    /// <param name="azureOpenAIClient">Azure OpenAI client used for query embeddings.</param>
    /// <param name="embeddingDeploymentName">Embedding model deployment name (must produce 1536-dim vectors to match the index).</param>
    /// <param name="credential">Credential used to authenticate against Azure AI Search.</param>
    public LeanSearchRetriever(
        Uri searchEndpoint,
        AzureOpenAIClient azureOpenAIClient,
        string embeddingDeploymentName,
        Azure.Core.TokenCredential credential,
        ILogger<LeanSearchRetriever> logger)
    {
        // Bind a SearchClient to the prototype index. SearchIndexClient (used at startup) manages
        // schemas; SearchClient is the data-plane client used for query/upload operations.
        _searchClient = new SearchClient(searchEndpoint, LeanSearchIndex.IndexName, credential);
        _embeddingClient = azureOpenAIClient.GetEmbeddingClient(embeddingDeploymentName);
        _logger = logger;
    }

    /// <summary>
    /// Runs a hybrid (vector + keyword) query and returns the top matching chunks.
    /// </summary>
    /// <param name="query">Natural-language user query.</param>
    /// <param name="topK">Maximum number of results to return after hybrid fusion.</param>
    /// <param name="filter">Optional metadata filter narrowing the candidate set before scoring.</param>
    public async Task<IReadOnlyList<LeanSearchHit>> SearchAsync(
        string query,
        int topK = 5,
        LeanSearchFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<LeanSearchHit>();
        }

        // 1. Embed the query. The vector dimensions must match the index field (1536 for text-embedding-3-small).
        var embeddingResponse = await _embeddingClient
            .GenerateEmbeddingAsync(query, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var queryVector = embeddingResponse.Value.ToFloats();

        // 2. Build hybrid search options:
        //    - SearchText  => BM25 keyword scoring against the "content" field.
        //    - VectorQuery => ANN search against "contentVector".
        //    Azure AI Search fuses both score lists using Reciprocal Rank Fusion (RRF).
        var options = new SearchOptions
        {
            Size = topK,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryVector)
                    {
                        KNearestNeighborsCount = topK,
                        Fields = { "contentVector" }
                    }
                }
            },
            // Only project the fields the caller needs; keeps payload small.
            Select =
            {
                "chunkId", "docId", "content",
                "artifactType", "sectionType",
                "valueStream", "site", "updatedAt"
            }
        };

        var odataFilter = filter?.ToODataFilter();
        if (!string.IsNullOrWhiteSpace(odataFilter))
        {
            options.Filter = odataFilter;
        }

        _logger.LogDebug(
            "Lean hybrid search. TopK={TopK}, Filter={Filter}, QueryLength={QueryLength}",
            topK, odataFilter ?? "(none)", query.Length);

        var response = await _searchClient
            .SearchAsync<LeanSearchDocument>(query, options, cancellationToken)
            .ConfigureAwait(false);

        var hits = new List<LeanSearchHit>(capacity: topK);
        await foreach (var result in response.Value.GetResultsAsync().ConfigureAwait(false))
        {
            hits.Add(new LeanSearchHit(result.Document, result.Score ?? 0d));
        }

        return hits;
    }
}

/// <summary>
/// Optional metadata filter for narrowing the candidate set before hybrid scoring.
/// All provided values are AND-combined; unset properties are ignored.
/// </summary>
public sealed record LeanSearchFilter(
    string? ArtifactType = null,
    string? SectionType = null,
    string? ValueStream = null,
    string? Site = null,
    DateTimeOffset? UpdatedAfter = null)
{
    /// <summary>Converts the filter to an OData expression accepted by Azure AI Search.</summary>
    internal string? ToODataFilter()
    {
        var clauses = new List<string>(capacity: 5);

        // Escape single quotes per OData string literal rules ('' represents a single quote).
        static string Escape(string value) => value.Replace("'", "''");

        if (!string.IsNullOrWhiteSpace(ArtifactType)) clauses.Add($"artifactType eq '{Escape(ArtifactType)}'");
        if (!string.IsNullOrWhiteSpace(SectionType))  clauses.Add($"sectionType eq '{Escape(SectionType)}'");
        if (!string.IsNullOrWhiteSpace(ValueStream))  clauses.Add($"valueStream eq '{Escape(ValueStream)}'");
        if (!string.IsNullOrWhiteSpace(Site))         clauses.Add($"site eq '{Escape(Site)}'");
        if (UpdatedAfter is { } after)                clauses.Add($"updatedAt ge {after:O}");

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }
}

/// <summary>A single hybrid search hit with the fused relevance score.</summary>
public sealed record LeanSearchHit(LeanSearchDocument Document, double Score);

/// <summary>Projection of fields returned from the prototype Lean/Kaizen index.</summary>
public sealed class LeanSearchDocument
{
    public string ChunkId { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ArtifactType { get; set; }
    public string? SectionType { get; set; }
    public string? ValueStream { get; set; }
    public string? Site { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
