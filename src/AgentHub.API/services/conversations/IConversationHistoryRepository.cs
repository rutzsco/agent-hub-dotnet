namespace AgentHub.API.services.conversations;

/// <summary>
/// Persistence contract for chat conversation history. Implemented by Cosmos and in-memory variants.
/// </summary>
public interface IConversationHistoryRepository
{
    /// <summary>Returns all messages for a conversation, ordered chronologically.</summary>
    Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Appends a single message (user, assistant, system, or tool) to the conversation log.</summary>
    Task AppendMessageAsync(
        Guid conversationId,
        string role,
        string content,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);
}
