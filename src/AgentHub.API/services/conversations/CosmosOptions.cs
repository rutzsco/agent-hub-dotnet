namespace AgentHub.API.services.conversations;

public sealed class CosmosOptions
{
    public string AccountEndpoint { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public string ConversationContainerName { get; init; } = "conversation-messages";
    public string MemoryAuditContainerName { get; init; } = "memory-audit";
}
