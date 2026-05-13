using AgentHub.API.services.conversations;
using AgentHub.API.Services.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentHub.Tests;

public class CosmosMemoryAuditRepositoryTests
{
    // Note: These are integration tests that require a running Cosmos DB account.
    // For unit testing without a database, mock IMemoryAuditRepository in service tests.

    [Fact(Skip = "Integration test - requires Cosmos DB")]
    public async Task LogMemoryDeletionAsync_InsertsAuditEntry()
    {
        var options = BuildOptionsFromEnv();
        var repository = new CosmosMemoryAuditRepository(options, NullLogger<CosmosMemoryAuditRepository>.Instance);

        await repository.LogMemoryDeletionAsync(
            userId: "user123",
            memoryStoreName: "test-store",
            auditMessage: "Memory deletion initiated by user",
            wasSuccessful: true,
            errorMessage: null);

        var history = await repository.GetUserDeletionHistoryAsync("user123");
        Assert.NotEmpty(history);
        var entry = history.First();
        Assert.Equal("user123", entry.UserId);
        Assert.Equal("test-store", entry.MemoryStoreName);
        Assert.Equal("Memory deletion initiated by user", entry.AuditMessage);
        Assert.True(entry.WasSuccessful);
        Assert.Null(entry.ErrorMessage);
    }

    [Fact(Skip = "Integration test - requires Cosmos DB")]
    public async Task LogMemoryDeletionAsync_LogsFailureWithErrorMessage()
    {
        var options = BuildOptionsFromEnv();
        var repository = new CosmosMemoryAuditRepository(options, NullLogger<CosmosMemoryAuditRepository>.Instance);

        await repository.LogMemoryDeletionAsync(
            userId: "user456",
            memoryStoreName: "test-store",
            auditMessage: "Memory deletion failed - connection issue",
            wasSuccessful: false,
            errorMessage: "Connection timeout");

        var history = await repository.GetUserDeletionHistoryAsync("user456");
        Assert.NotEmpty(history);
        var entry = history.First();
        Assert.False(entry.WasSuccessful);
        Assert.Equal("Connection timeout", entry.ErrorMessage);
    }

    [Fact(Skip = "Integration test - requires Cosmos DB")]
    public async Task GetUserDeletionHistoryAsync_ReturnsEntriesInDescendingOrder()
    {
        var options = BuildOptionsFromEnv();
        var repository = new CosmosMemoryAuditRepository(options, NullLogger<CosmosMemoryAuditRepository>.Instance);
        var userId = $"user-{Guid.NewGuid()}";

        await repository.LogMemoryDeletionAsync(userId, "store1", "First deletion attempt", true);
        await repository.LogMemoryDeletionAsync(userId, "store2", "Second deletion attempt", true);
        await repository.LogMemoryDeletionAsync(userId, "store3", "Third deletion failed", false, "Test error");

        var history = await repository.GetUserDeletionHistoryAsync(userId);

        Assert.Equal(3, history.Count);
        Assert.Equal("store3", history[0].MemoryStoreName);
        Assert.Equal("store2", history[1].MemoryStoreName);
        Assert.Equal("store1", history[2].MemoryStoreName);
    }

    [Fact]
    public void Constructor_ThrowsWhenEndpointIsEmpty()
    {
        var options = new CosmosOptions
        {
            AccountEndpoint = "",
            DatabaseName = "test",
            ConversationContainerName = "conversation-messages",
            MemoryAuditContainerName = "memory-audit"
        };
        Assert.Throws<InvalidOperationException>(
            () => new CosmosMemoryAuditRepository(options, NullLogger<CosmosMemoryAuditRepository>.Instance));
    }

    [Fact]
    public void Constructor_ThrowsWhenDatabaseNameIsEmpty()
    {
        var options = new CosmosOptions
        {
            AccountEndpoint = "https://example.documents.azure.com:443/",
            DatabaseName = "",
            ConversationContainerName = "conversation-messages",
            MemoryAuditContainerName = "memory-audit"
        };
        Assert.Throws<InvalidOperationException>(
            () => new CosmosMemoryAuditRepository(options, NullLogger<CosmosMemoryAuditRepository>.Instance));
    }

    private static CosmosOptions BuildOptionsFromEnv() => new()
    {
        AccountEndpoint = Environment.GetEnvironmentVariable("COSMOS_ACCOUNT_ENDPOINT")
            ?? throw new InvalidOperationException("COSMOS_ACCOUNT_ENDPOINT not configured"),
        DatabaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "agent-hub",
        ConversationContainerName = Environment.GetEnvironmentVariable("COSMOS_CONVERSATION_CONTAINER_NAME")
            ?? "conversation-messages",
        MemoryAuditContainerName = Environment.GetEnvironmentVariable("COSMOS_MEMORY_AUDIT_CONTAINER_NAME")
            ?? "memory-audit"
    };
}

