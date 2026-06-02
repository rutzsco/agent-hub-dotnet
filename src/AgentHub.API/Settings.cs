using Microsoft.Extensions.Configuration;

namespace AgentHub.API;

/// <summary>
/// Immutable, strongly-typed view of AgentHub configuration.
/// Bound once at startup via <see cref="Load"/> and registered as a DI singleton so
/// downstream services depend on this type rather than on <see cref="IConfiguration"/>.
/// </summary>
/// <remarks>
/// Every value supports two configuration sources for convenience:
/// the hierarchical <c>AgentHub:*</c> section (preferred for appsettings.json) and
/// flat <c>SCREAMING_SNAKE_CASE</c> environment variables (preferred for containers / CI).
/// </remarks>
public class Settings
{
    /// <summary>Azure AI project endpoint used by Foundry clients. Optional at construction time; required at use via <see cref="RequireAzureAIProjectEndpoint"/>.</summary>
    public Uri? AzureAIProjectEndpoint { get; init; }
    /// <summary>Optional dedicated Azure OpenAI endpoint for the demo AOAI agent.</summary>
    public Uri? AzureOpenAIEndpoint { get; init; }
    /// <summary>Model deployment name used for chat completions (must exist in the Foundry project).</summary>
    public string AzureAIModelDeploymentName { get; init; } = "gpt-4o-mini";
    public string? AzureAIApiKey { get; init; }
    public string? ApimSubscriptionKey { get; init; }
    /// <summary>Optional override for the Foundry agent name; falls back to per-agent defaults.</summary>
    public string? FoundryAgentName { get; init; }
    /// <summary>Optional Entra tenant id pinned across all credential flows.</summary>
    public string? AzureTenantId { get; init; }
    /// <summary>Optional override for the memory agent's system prompt.</summary>
    public string? MemoryAgentInstructions { get; init; }

    /// <summary>
    /// Builds a <see cref="Azure.Identity.DefaultAzureCredential"/> pinned to <see cref="AzureTenantId"/>
    /// when configured. Centralized so every Azure SDK client in the app authenticates identically.
    /// </summary>
    public Azure.Identity.DefaultAzureCredential CreateAzureCredential()
    {
        var options = new Azure.Identity.DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(AzureTenantId))
        {
            // Pin tenant across every credential source to avoid silent fallback to the wrong tenant
            // when the developer is signed into multiple tenants (VS, Azure CLI, browser, etc.).
            options.TenantId = AzureTenantId;
            options.VisualStudioTenantId = AzureTenantId;
            options.SharedTokenCacheTenantId = AzureTenantId;
            options.InteractiveBrowserTenantId = AzureTenantId;
        }
        return new Azure.Identity.DefaultAzureCredential(options);
    }
    /// <summary>Foundry memory store name (created on first run if missing).</summary>
    public string MemoryStoreName { get; init; } = "agent-hub-memory";
    /// <summary>Embedding model used by the Foundry memory store.</summary>
    public string MemoryEmbeddingModel { get; init; } = "text-embedding-3-small";
    /// <summary>Cosmos DB account endpoint; when null, conversation history falls back to in-memory.</summary>
    public string? CosmosAccountEndpoint { get; init; }
    /// <summary>Cosmos DB database name (defaults to "agent-hub" if not specified).</summary>
    public string? CosmosDatabaseName { get; init; }
    /// <summary>Cosmos container for persisted conversation messages.</summary>
    public string CosmosConversationContainerName { get; init; } = "conversation-messages";
    /// <summary>Cosmos container for memory deletion audit entries.</summary>
    public string CosmosMemoryAuditContainerName { get; init; } = "memory-audit";
    /// <summary>Azure AI Search endpoint; when null, search index registration is skipped.</summary>
    public Uri? AzureSearchEndpoint { get; init; }
    public string AzureSearchEmbeddingDeployment { get; init; } = "text-embedding-3-small";
    public string AzureSearchEmbeddingModel { get; init; } = "text-embedding-3-small";
    public int AzureSearchEmbeddingDimensions { get; init; } = 1536;
    /// <summary>Cosmos container for KnowledgeBase PDF chunks and vectors.</summary>
    public string CosmosKnowledgeBaseContainerName { get; init; } = "knowledge-base-chunks";
    public int AzureSearchTopK { get; init; } = 5;
    /// <summary>Azure Blob Storage container URI containing KnowledgeBase source PDFs.</summary>
    public Uri? KnowledgeBaseBlobContainerUri { get; init; }
    /// <summary>Optional default blob prefix/folder to index for KnowledgeBase ingestion.</summary>
    public string? KnowledgeBaseBlobPrefix { get; init; }
    /// <summary>Azure AI Document Intelligence endpoint used to extract text from PDFs.</summary>
    public Uri? KnowledgeBaseDocumentIntelligenceEndpoint { get; init; }
    public int KnowledgeBaseChunkMaxCharacters { get; init; } = 3500;
    public int KnowledgeBaseChunkOverlapCharacters { get; init; } = 400;
    public int KnowledgeBaseDefaultMaxFiles { get; init; } = 10;
    public int KnowledgeBaseMaxChunksPerDocument { get; init; } = 500;

    /// <summary>
    /// When true, the client-side <c>LeanKnowledgeTool</c> is NOT registered on the agent.
    /// Use this to demo a Foundry agent that has the Azure AI Search knowledge source
    /// attached server-side (configured in the Foundry portal). Default: false (client-side tool).
    /// </summary>
    public bool UseServerSideSearchTool { get; init; }

    /// <summary>
    /// Reads configuration from the <c>AgentHub</c> section (with env-var fallbacks) and builds a populated <see cref="Settings"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="AzureAIProjectEndpoint"/> is not configured.</exception>
    public static Settings Load(IConfiguration configuration)
    {
        var agentHubSection = configuration.GetSection("AgentHub");

        var endpoint = GetOptionalValue(
            agentHubSection["AzureAIProjectEndpoint"],
            configuration["AZURE_AI_PROJECT_ENDPOINT"]);

        var modelDeploymentName = GetOptionalValue(
            agentHubSection["AzureAIModelDeploymentName"],
            configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"])
            ?? "gpt-4o-mini";

        var azureOpenAIEndpoint = GetOptionalValue(
            agentHubSection["AzureOpenAIEndpoint"],
            configuration["AZURE_OPENAI_ENDPOINT"]);

        var azureAIApiKey = GetOptionalValue(
            agentHubSection["AzureAIApiKey"],
            configuration["AZURE_AI_API_KEY"],
            configuration["AZURE_OPENAI_API_KEY"]);

        var apimSubscriptionKey = GetOptionalValue(
            agentHubSection["ApimSubscriptionKey"],
            configuration["APIM_SUBSCRIPTION_KEY"]);

        var azureTenantId = agentHubSection["AzureTenantId"]
            ?? configuration["AZURE_TENANT_ID"];

        // MemoryAgentInstructions accepts either a single string OR a JSON array of lines.
        // The array form keeps multi-line prompts readable in appsettings.json without escaped newlines.
        var instructionsSection = agentHubSection.GetSection("MemoryAgentInstructions");
        string? memoryAgentInstructions = null;
        if (instructionsSection.Exists())
        {
            if (instructionsSection.Value is { Length: > 0 } singleValue)
            {
                memoryAgentInstructions = singleValue;
            }
            else
            {

                var lines = instructionsSection.GetChildren()
                    .Select(c => c.Value ?? string.Empty)
                    .ToArray();
                if (lines.Length > 0)
                {
                    memoryAgentInstructions = string.Join('\n', lines);
                }
            }
        }
        memoryAgentInstructions ??= configuration["AZURE_AI_MEMORY_AGENT_INSTRUCTIONS"];

        //var memoryStoreName = agentHubSection["MemoryStoreName"]
        //    ?? configuration["AZURE_AI_MEMORY_STORE_NAME"];
        var foundryAgentName = GetOptionalValue(
            agentHubSection["FoundryAgentName"],
            configuration["AZURE_AI_FOUNDRY_AGENT_NAME"]);

        var memoryStoreName = GetOptionalValue(
            agentHubSection["MemoryStoreName"],
            configuration["AZURE_AI_MEMORY_STORE_NAME"])
            ?? "agent-hub-memory";

        var memoryEmbeddingModel = GetOptionalValue(
            agentHubSection["MemoryEmbeddingModel"],
            configuration["AZURE_AI_MEMORY_EMBEDDING_MODEL"])
            ?? "text-embedding-3-small";

        var cosmosSection = agentHubSection.GetSection("Cosmos");

        var cosmosAccountEndpoint = GetOptionalValue(
            cosmosSection["AccountEndpoint"],
            configuration["COSMOS_ACCOUNT_ENDPOINT"]);

        var cosmosDatabaseName = GetOptionalValue(
            cosmosSection["DatabaseName"],
            configuration["COSMOS_DATABASE_NAME"]);

        var cosmosConversationContainerName = GetOptionalValue(
            cosmosSection["ConversationContainerName"],
            configuration["COSMOS_CONVERSATION_CONTAINER_NAME"])
            ?? "conversation-messages";

        var cosmosMemoryAuditContainerName = GetOptionalValue(
            cosmosSection["MemoryAuditContainerName"],
            configuration["COSMOS_MEMORY_AUDIT_CONTAINER_NAME"])
            ?? "memory-audit";

        var cosmosKnowledgeBaseContainerName = GetOptionalValue(
            cosmosSection["KnowledgeBaseContainerName"],
            configuration["COSMOS_KNOWLEDGE_BASE_CONTAINER_NAME"])
            ?? "knowledge-base-chunks";

        var knowledgeBaseSection = agentHubSection.GetSection("KnowledgeBase");
        var knowledgeBaseBlobContainerUriValue = GetOptionalValue(
            knowledgeBaseSection["BlobContainerUri"],
            configuration["KNOWLEDGE_BASE_BLOB_CONTAINER_URI"]);
        var knowledgeBaseBlobContainerUri = string.IsNullOrWhiteSpace(knowledgeBaseBlobContainerUriValue)
            ? null
            : new Uri(knowledgeBaseBlobContainerUriValue);

        var knowledgeBaseBlobPrefix = GetOptionalValue(
            knowledgeBaseSection["BlobPrefix"],
            configuration["KNOWLEDGE_BASE_BLOB_PREFIX"]);

        var knowledgeBaseDocumentIntelligenceEndpointValue = GetOptionalValue(
            knowledgeBaseSection["DocumentIntelligenceEndpoint"],
            configuration["DOCUMENT_INTELLIGENCE_ENDPOINT"]);
        var knowledgeBaseDocumentIntelligenceEndpoint = string.IsNullOrWhiteSpace(knowledgeBaseDocumentIntelligenceEndpointValue)
            ? null
            : new Uri(knowledgeBaseDocumentIntelligenceEndpointValue);

        var knowledgeBaseChunkMaxCharacters = GetPositiveInt(
            knowledgeBaseSection["ChunkMaxCharacters"],
            configuration["KNOWLEDGE_BASE_CHUNK_MAX_CHARACTERS"],
            3500);

        var knowledgeBaseChunkOverlapCharacters = GetPositiveInt(
            knowledgeBaseSection["ChunkOverlapCharacters"],
            configuration["KNOWLEDGE_BASE_CHUNK_OVERLAP_CHARACTERS"],
            400);

        if (knowledgeBaseChunkOverlapCharacters >= knowledgeBaseChunkMaxCharacters)
        {
            knowledgeBaseChunkOverlapCharacters = Math.Max(0, knowledgeBaseChunkMaxCharacters / 10);
        }

        var knowledgeBaseDefaultMaxFiles = GetPositiveInt(
            knowledgeBaseSection["DefaultMaxFiles"],
            configuration["KNOWLEDGE_BASE_DEFAULT_MAX_FILES"],
            10);

        var knowledgeBaseMaxChunksPerDocument = GetPositiveInt(
            knowledgeBaseSection["MaxChunksPerDocument"],
            configuration["KNOWLEDGE_BASE_MAX_CHUNKS_PER_DOCUMENT"],
            500);

        var azureSearchEndpointValue = agentHubSection.GetSection("AzureSearch")["Endpoint"]
            ?? agentHubSection["AzureSearchEndpoint"]
            ?? configuration["AZURE_SEARCH_ENDPOINT"];
        Uri? azureSearchEndpoint = string.IsNullOrWhiteSpace(azureSearchEndpointValue)
            ? null
            : new Uri(azureSearchEndpointValue);

        var azureSearchTopKValue = agentHubSection.GetSection("AzureSearch")["TopK"]
            ?? configuration["AZURE_SEARCH_TOP_K"];
        var azureSearchTopK = int.TryParse(azureSearchTopKValue, out var parsedTopK) && parsedTopK > 0
            ? parsedTopK
            : 5;

        var useServerSideSearchToolValue = agentHubSection.GetSection("AzureSearch")["UseServerSideTool"]
            ?? configuration["AZURE_SEARCH_USE_SERVER_SIDE_TOOL"];
        var useServerSideSearchTool = bool.TryParse(useServerSideSearchToolValue, out var parsedFlag) && parsedFlag;

        return new Settings
        {
            AzureAIProjectEndpoint = endpoint is null ? null : new Uri(endpoint),
            AzureOpenAIEndpoint = azureOpenAIEndpoint is null ? null : new Uri(azureOpenAIEndpoint),
            AzureAIModelDeploymentName = modelDeploymentName,
            AzureAIApiKey = azureAIApiKey,
            ApimSubscriptionKey = apimSubscriptionKey,
            FoundryAgentName = foundryAgentName,
            AzureTenantId = azureTenantId,
            MemoryAgentInstructions = memoryAgentInstructions,
            MemoryStoreName = memoryStoreName,
            MemoryEmbeddingModel = memoryEmbeddingModel,
            CosmosAccountEndpoint = cosmosAccountEndpoint,
            CosmosDatabaseName = cosmosDatabaseName,
            CosmosConversationContainerName = cosmosConversationContainerName,
            CosmosMemoryAuditContainerName = cosmosMemoryAuditContainerName,
            CosmosKnowledgeBaseContainerName = cosmosKnowledgeBaseContainerName,
            AzureSearchEndpoint = azureSearchEndpoint is null ? null : azureSearchEndpoint,
            AzureSearchTopK = azureSearchTopK,
            KnowledgeBaseBlobContainerUri = knowledgeBaseBlobContainerUri,
            KnowledgeBaseBlobPrefix = knowledgeBaseBlobPrefix,
            KnowledgeBaseDocumentIntelligenceEndpoint = knowledgeBaseDocumentIntelligenceEndpoint,
            KnowledgeBaseChunkMaxCharacters = knowledgeBaseChunkMaxCharacters,
            KnowledgeBaseChunkOverlapCharacters = knowledgeBaseChunkOverlapCharacters,
            KnowledgeBaseDefaultMaxFiles = knowledgeBaseDefaultMaxFiles,
            KnowledgeBaseMaxChunksPerDocument = knowledgeBaseMaxChunksPerDocument,
            UseServerSideSearchTool = useServerSideSearchTool
        };
    }

    private static string? GetOptionalValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static int GetPositiveInt(string? sectionValue, string? envValue, int defaultValue)
    {
        var value = GetOptionalValue(sectionValue, envValue);
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : defaultValue;
    }

    public Uri RequireAzureAIProjectEndpoint()
    {
        return AzureAIProjectEndpoint
            ?? throw new InvalidOperationException(
                "Foundry agents are not configured. Set AgentHub:AzureAIProjectEndpoint or AZURE_AI_PROJECT_ENDPOINT to enable Foundry-backed endpoints.");
    }

    public Uri RequireAzureOpenAIEndpoint()
    {
        return AzureOpenAIEndpoint
            ?? AzureAIProjectEndpoint
            ?? throw new InvalidOperationException(
                "Azure OpenAI endpoint is not configured. Set AgentHub:AzureOpenAIEndpoint, AZURE_OPENAI_ENDPOINT, AgentHub:AzureAIProjectEndpoint, or AZURE_AI_PROJECT_ENDPOINT.");
    }
}
