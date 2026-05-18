using System.Collections.Concurrent;

namespace AgentHub.API.services.conversations;

public sealed class InMemoryConversationHistoryRepository : IConversationHistoryRepository
{
    private readonly ConcurrentDictionary<Guid, List<ConversationMessage>> _messages = new();

    public Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!_messages.TryGetValue(conversationId, out var messages))
        {
            return Task.FromResult<IReadOnlyList<ConversationMessage>>(Array.Empty<ConversationMessage>());
        }

        lock (messages)
        {
            return Task.FromResult<IReadOnlyList<ConversationMessage>>(messages.ToArray());
        }
    }

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
