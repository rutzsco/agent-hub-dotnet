using System.Collections.Concurrent;

namespace AgentHub.API.services.conversations;

/// <summary>
/// Process-local <see cref="IConversationHistoryRepository"/> for dev/test scenarios when Cosmos is not configured.
/// History is lost on process restart and not shared across instances.
/// </summary>
public sealed class InMemoryConversationHistoryRepository : IConversationHistoryRepository
{
    private readonly ConcurrentDictionary<Guid, List<ConversationMessage>> _messages = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!_messages.TryGetValue(conversationId, out var messages))
        {
            return Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());
        }

        // Snapshot under the per-conversation lock so callers see a consistent list even during concurrent appends.
        lock (messages)
        {
            return Task.FromResult<IReadOnlyList<ConversationMessage>>(messages.ToArray());
        }
    }

    /// <inheritdoc />
    public Task AppendMessageAsync(
        Guid conversationId,
        string role,
        string content,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var messages = _messages.GetOrAdd(conversationId, _ => []);

        lock (messages)
        {
            messages.Add(new ConversationMessage(
                Guid.NewGuid().ToString(),
                conversationId,
                role,
                content,
                createdAt));
        }

        return Task.CompletedTask;
    }
}
