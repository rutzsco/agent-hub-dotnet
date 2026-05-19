using AgentHub.API.services.conversations;

namespace AgentHub.API.services.session;

/// <summary>
/// Abstraction over the lifecycle of agent conversation sessions:
/// resolves/creates a session for an incoming request, persists turns,
/// and exposes history for replay or display.
/// </summary>
public interface IConversationSessionManager
{
    /// <summary>
    /// Resolves an existing in-memory session or creates a new one. When a stored conversation
    /// id is supplied, prior history is loaded so the caller can decide whether to replay it.
    /// </summary>
    Task<ConversationSessionContext> GetOrCreateSessionAsync(
        Guid? conversationId,
        Func<CancellationToken, Task<object>> createSession,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a single user/assistant exchange to durable storage.</summary>
    Task AppendTurnAsync(
        Guid conversationId,
        string userMessage,
        string assistantMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a conversation as service-managed (Foundry-owned thread). No history is persisted locally;
    /// only the mapping between our id and the service's id is tracked.
    /// </summary>
    Task SaveServiceManagedConversationAsync(
        Guid conversationId,
        string serviceConversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the persisted message history for a conversation.</summary>
    Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}
