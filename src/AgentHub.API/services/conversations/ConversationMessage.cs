namespace AgentHub.API.services.conversations;

public sealed record ConversationMessage(
    string Id,
    Guid ConversationId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
