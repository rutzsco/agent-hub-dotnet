using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace AgentHub.API.services.search;

/// <summary>
/// Prototype: programmatic creation of a minimal Azure AI Search index for Lean/Kaizen artifacts.
/// Hybrid retrieval (vector + keyword) with a small set of domain filters.
/// </summary>
public static class LeanSearchIndex
{
    /// <summary>Logical name of the Azure AI Search index this class manages.</summary>
    public const string IndexName = "lean-kaizen-proto";

    // Match the embedding model dimensions (text-embedding-3-small = 1536).
    private const int EmbeddingDimensions = 1536;
    private const string VectorProfileName = "hnsw-cosine";
    private const string VectorAlgorithmName = "hnsw-default";

    /// <summary>
    /// Idempotently creates (or updates) the index schema. Safe to call on every app startup.
    /// </summary>
    public static async Task EnsureCreatedAsync(SearchIndexClient client, CancellationToken ct = default)
    {
        var index = new SearchIndex(IndexName)
        {
            Fields =
            {
                new SimpleField("chunkId", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SimpleField("docId", SearchFieldDataType.String) { IsFilterable = true },

                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },

                new VectorSearchField("contentVector", EmbeddingDimensions, VectorProfileName),

                // Lean domain
                new SimpleField("artifactType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField("sectionType",  SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },

                // Org context
                new SimpleField("valueStream", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField("site",        SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },

                // Freshness
                new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
            },
            VectorSearch = new VectorSearch
            {
                Profiles  = { new VectorSearchProfile(VectorProfileName, VectorAlgorithmName) },
                Algorithms = { new HnswAlgorithmConfiguration(VectorAlgorithmName) }
            }
        };

        await client.CreateOrUpdateIndexAsync(index, cancellationToken: ct).ConfigureAwait(false);
    }
}
