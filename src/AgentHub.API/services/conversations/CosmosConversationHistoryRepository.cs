using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;

namespace AgentHub.API.services.conversations;

public sealed class CosmosConversationHistoryRepository : CosmosRepositoryBase, IConversationHistoryRepository
{
    public CosmosConversationHistoryRepository(
        CosmosOptions options,
        ILogger<CosmosConversationHistoryRepository> logger)
        : base(options, options.ConversationContainerName, "/conversationId", logger)
    {
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);
        var partitionKey = conversationId.ToString();

        Logger.LogDebug("Fetching messages for conversation {ConversationId}", conversationId);

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.conversationId = @id ORDER BY c.createdAt ASC")
            .WithParameter("@id", partitionKey);

        var messages = new List<ConversationMessage>();

        using var feed = container.GetItemQueryIterator<ConversationDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) });

        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellationToken);
            foreach (var doc in page)
            {
                messages.Add(new ConversationMessage(
                    Id: doc.id,
                    ConversationId: conversationId,
                    Role: doc.role,
                    Content: doc.content,
                    CreatedAt: doc.createdAt));
            }
        }

        Logger.LogDebug("Fetched {Count} messages for conversation {ConversationId}", messages.Count, conversationId);
        return messages;
    }

    public async Task AppendMessageAsync(
        Guid conversationId,
        string role,
        string content,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);
        var partitionKey = conversationId.ToString();

        var doc = new ConversationDocument(
            id: Guid.NewGuid().ToString(),
            conversationId: partitionKey,
            role: role,
            content: content,
            createdAt: createdAt);

        Logger.LogDebug("Appending {Role} message to conversation {ConversationId}", role, conversationId);
        await container.CreateItemAsync(doc, new PartitionKey(partitionKey), cancellationToken: cancellationToken);
        Logger.LogDebug("Appended {Role} message to conversation {ConversationId}", role, conversationId);
    }

    private sealed record ConversationDocument(
        string id,
        string conversationId,
        string role,
        string content,
        DateTimeOffset createdAt);
}
