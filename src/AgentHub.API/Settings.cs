using Microsoft.Extensions.Configuration;

namespace AgentHub.API;

public class Settings
{
    public required Uri AzureAIProjectEndpoint { get; init; }
    public Uri? AzureOpenAIEndpoint { get; init; }
    public string AzureAIModelDeploymentName { get; init; } = "gpt-4o-mini";
    public string? AzureAIApiKey { get; init; }
    public string? FoundryAgentName { get; init; }
    public string MemoryStoreName { get; init; } = "agent-hub-memory";
    public string MemoryEmbeddingModel { get; init; } = "text-embedding-3-small";
    public string? CosmosAccountEndpoint { get; init; }
    public string? CosmosDatabaseName { get; init; }
    public string CosmosConversationContainerName { get; init; } = "conversation-messages";
    public string CosmosMemoryAuditContainerName { get; init; } = "memory-audit";

    public static Settings Load(IConfiguration configuration)
    {
        var agentHubSection = configuration.GetSection("AgentHub");

        var endpoint = agentHubSection["AzureAIProjectEndpoint"]
            ?? configuration["AZURE_AI_PROJECT_ENDPOINT"]
            ?? throw new InvalidOperationException(
                "Azure AI project endpoint is not configured. Set AgentHub:AzureAIProjectEndpoint or AZURE_AI_PROJECT_ENDPOINT.");

        var modelDeploymentName = agentHubSection["AzureAIModelDeploymentName"]
            ?? configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"]
            ?? "gpt-4o-mini";

        var azureOpenAIEndpoint = GetOptionalValue(
            agentHubSection["AzureOpenAIEndpoint"],
            configuration["AZURE_OPENAI_ENDPOINT"]);

        var azureAIApiKey = GetOptionalValue(
            agentHubSection["AzureAIApiKey"],
            configuration["AZURE_AI_API_KEY"],
            configuration["AZURE_OPENAI_API_KEY"]);

        var foundryAgentName = agentHubSection["FoundryAgentName"]
            ?? configuration["AZURE_AI_FOUNDRY_AGENT_NAME"];

        var memoryStoreName = agentHubSection["MemoryStoreName"]
            ?? configuration["AZURE_AI_MEMORY_STORE_NAME"]
            ?? "agent-hub-memory";

        var memoryEmbeddingModel = agentHubSection["MemoryEmbeddingModel"]
            ?? configuration["AZURE_AI_MEMORY_EMBEDDING_MODEL"]
            ?? "text-embedding-3-small";

        var cosmosAccountEndpoint = agentHubSection.GetSection("Cosmos")["AccountEndpoint"]
            ?? configuration["COSMOS_ACCOUNT_ENDPOINT"];

        var cosmosDatabaseName = agentHubSection.GetSection("Cosmos")["DatabaseName"]
            ?? configuration["COSMOS_DATABASE_NAME"];

        var cosmosConversationContainerName = agentHubSection.GetSection("Cosmos")["ConversationContainerName"]
            ?? configuration["COSMOS_CONVERSATION_CONTAINER_NAME"]
            ?? "conversation-messages";

        var cosmosMemoryAuditContainerName = agentHubSection.GetSection("Cosmos")["MemoryAuditContainerName"]
            ?? configuration["COSMOS_MEMORY_AUDIT_CONTAINER_NAME"]
            ?? "memory-audit";

        return new Settings
        {
            AzureAIProjectEndpoint = new Uri(endpoint),
            AzureOpenAIEndpoint = azureOpenAIEndpoint is null ? null : new Uri(azureOpenAIEndpoint),
            AzureAIModelDeploymentName = modelDeploymentName,
            AzureAIApiKey = azureAIApiKey,
            FoundryAgentName = foundryAgentName,
            MemoryStoreName = memoryStoreName,
            MemoryEmbeddingModel = memoryEmbeddingModel,
            CosmosAccountEndpoint = cosmosAccountEndpoint,
            CosmosDatabaseName = cosmosDatabaseName,
            CosmosConversationContainerName = cosmosConversationContainerName,
            CosmosMemoryAuditContainerName = cosmosMemoryAuditContainerName
        };
    }

    private static string? GetOptionalValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
