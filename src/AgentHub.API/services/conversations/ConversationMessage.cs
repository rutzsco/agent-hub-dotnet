namespace AgentHub.API.services.conversations;

public sealed record ConversationMessage(
    long Id,
    Guid ConversationId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);
