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
    private const string VectorizerName = "aoai-embed-small";

    /// <summary>
    /// Idempotently creates (or updates) the index schema. Safe to call on every app startup.
    /// </summary>
    /// <remarks>
    /// When <paramref name="settings"/> exposes an Azure OpenAI endpoint, the index is configured
    /// with an <c>AzureOpenAIVectorizer</c> bound to <see cref="Settings.AzureSearchEmbeddingDeployment"/>.
    /// This lets clients (e.g., the Foundry server-side AI Search tool) issue plain-text vector
    /// queries — the index embeds them server-side. Without it, callers must pre-embed queries
    /// (the path used by <c>LeanSearchRetriever</c>).
    /// </remarks>
    public static async Task EnsureCreatedAsync(SearchIndexClient client, Settings settings, CancellationToken ct = default)
    {
        var vectorSearch = new VectorSearch
        {
            Algorithms = { new HnswAlgorithmConfiguration(VectorAlgorithmName) }
        };

        // Attach an Azure OpenAI vectorizer only when an AOAI endpoint is configured.
        // The Search service authenticates to AOAI via its own managed identity, so grant it
        // the 'Cognitive Services OpenAI User' role on the AOAI resource for this to work.
        if (settings.AzureOpenAIEndpoint is not null)
        {
            vectorSearch.Vectorizers.Add(new AzureOpenAIVectorizer(VectorizerName)
            {
                Parameters = new AzureOpenAIVectorizerParameters
                {
                    ResourceUri = settings.AzureOpenAIEndpoint,
                    DeploymentName = settings.AzureSearchEmbeddingDeployment,
                    ModelName = settings.AzureSearchEmbeddingModel
                }
            });

            vectorSearch.Profiles.Add(new VectorSearchProfile(VectorProfileName, VectorAlgorithmName)
            {
                VectorizerName = VectorizerName
            });
        }
        else
    {
            // No vectorizer — pre-embedded query vectors required at query time.
            vectorSearch.Profiles.Add(new VectorSearchProfile(VectorProfileName, VectorAlgorithmName));
        }

        var index = new SearchIndex(IndexName)
        {
            Fields =
            {
                new SimpleField("chunkId", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
                new SimpleField("docId", SearchFieldDataType.String) { IsFilterable = true },

                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },

                new VectorSearchField("contentVector", settings.AzureSearchEmbeddingDimensions, VectorProfileName),

                // Lean domain
                new SimpleField("artifactType", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField("sectionType",  SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },

                // Org context
                new SimpleField("valueStream", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField("site",        SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },

                // Freshness
                new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true }
            },
            VectorSearch = vectorSearch
        };

        await client.CreateOrUpdateIndexAsync(index, cancellationToken: ct).ConfigureAwait(false);
    }
}
