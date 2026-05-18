using AgentHub.API.services.conversations;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace AgentHub.API.Services.Memory;

public sealed class CosmosMemoryAuditRepository : CosmosRepositoryBase, IMemoryAuditRepository
{
    public CosmosMemoryAuditRepository(
        CosmosOptions options,
        ILogger<CosmosMemoryAuditRepository> logger)
        : base(options, options.MemoryAuditContainerName, "/userId", logger)
    {
    }

    public async Task LogMemoryDeletionAsync(
        string userId,
        string memoryStoreName,
        string auditMessage,
        bool wasSuccessful,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);

        var doc = new AuditDocument(
            id: Guid.NewGuid().ToString(),
            userId: userId,
            memoryStoreName: memoryStoreName,
            auditMessage: auditMessage,
            wasSuccessful: wasSuccessful,
            errorMessage: errorMessage,
            createdAt: DateTimeOffset.UtcNow);

        Logger.LogDebug(
            "Logging memory deletion. UserId={UserId}, MemoryStore={MemoryStore}, Success={Success}",
            userId, memoryStoreName, wasSuccessful);

        await container.CreateItemAsync(doc, new PartitionKey(userId), cancellationToken: cancellationToken);

        Logger.LogInformation(
            "Memory deletion audit logged. UserId={UserId}, MemoryStore={MemoryStore}, AuditId={AuditId}, Success={Success}",
            userId, memoryStoreName, doc.id, wasSuccessful);
    }

    public async Task<IReadOnlyList<MemoryDeletionAuditEntry>> GetUserDeletionHistoryAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var container = await GetContainerAsync(cancellationToken);

        Logger.LogDebug("Retrieving deletion history for UserId={UserId}", userId);

        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.userId = @userId ORDER BY c.createdAt DESC")
            .WithParameter("@userId", userId);

        var entries = new List<MemoryDeletionAuditEntry>();

        using var feed = container.GetItemQueryIterator<AuditDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(cancellationToken);
            foreach (var doc in page)
            {
                entries.Add(new MemoryDeletionAuditEntry(
                    Id: doc.id,
                    UserId: doc.userId,
                    MemoryStoreName: doc.memoryStoreName,
                    AuditMessage: doc.auditMessage,
                    WasSuccessful: doc.wasSuccessful,
                    ErrorMessage: doc.errorMessage,
                    CreatedAt: doc.createdAt));
            }
        }

        Logger.LogInformation(
            "Retrieved {Count} deletion audit entries for UserId={UserId}", entries.Count, userId);

        return entries;
    }

    private sealed record AuditDocument(
        string id,
        string userId,
        string memoryStoreName,
        string auditMessage,
        bool wasSuccessful,
        string? errorMessage,
        DateTimeOffset createdAt);
}
