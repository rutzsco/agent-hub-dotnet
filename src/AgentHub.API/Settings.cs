using Microsoft.Extensions.Configuration;

namespace AgentHub.API;

public class Settings
{
    public Uri? AzureAIProjectEndpoint { get; init; }
    public Uri? AzureOpenAIEndpoint { get; init; }
    public string AzureAIModelDeploymentName { get; init; } = "gpt-4o-mini";
    public string? AzureAIApiKey { get; init; }
    public string? ApimSubscriptionKey { get; init; }
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

        return new Settings
        {
            AzureAIProjectEndpoint = endpoint is null ? null : new Uri(endpoint),
            AzureOpenAIEndpoint = azureOpenAIEndpoint is null ? null : new Uri(azureOpenAIEndpoint),
            AzureAIModelDeploymentName = modelDeploymentName,
            AzureAIApiKey = azureAIApiKey,
            ApimSubscriptionKey = apimSubscriptionKey,
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
