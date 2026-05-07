using AgentHub.API.services.conversations;
using AgentHub.API.Services.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentHub.Tests;

public class PostgresMemoryAuditRepositoryTests
{
    // Note: These are integration tests that require a running PostgreSQL instance.
    // For unit testing without a database, you can mock IMemoryAuditRepository in service tests.
    
    [Fact(Skip = "Integration test - requires PostgreSQL")]
    public async Task LogMemoryDeletionAsync_InsertsAuditEntry()
    {
        // Arrange
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING not configured");
        var options = new PostgresConversationOptions { ConnectionString = connectionString };
        var repository = new PostgresMemoryAuditRepository(options, NullLogger<PostgresMemoryAuditRepository>.Instance);

        // Act
        await repository.LogMemoryDeletionAsync(
            userId: "user123",
            memoryStoreName: "test-store",
            auditMessage: "Memory deletion initiated by user",
            wasSuccessful: true,
            errorMessage: null);

        // Assert - retrieve the entry
        var history = await repository.GetUserDeletionHistoryAsync("user123");
        Assert.NotEmpty(history);
        var entry = history.First();
        Assert.Equal("user123", entry.UserId);
        Assert.Equal("test-store", entry.MemoryStoreName);
        Assert.Equal("Memory deletion initiated by user", entry.AuditMessage);
        Assert.True(entry.WasSuccessful);
        Assert.Null(entry.ErrorMessage);
    }

    [Fact(Skip = "Integration test - requires PostgreSQL")]
    public async Task LogMemoryDeletionAsync_LogsFailureWithErrorMessage()
    {
        // Arrange
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING not configured");
        var options = new PostgresConversationOptions { ConnectionString = connectionString };
        var repository = new PostgresMemoryAuditRepository(options, NullLogger<PostgresMemoryAuditRepository>.Instance);

        // Act
        await repository.LogMemoryDeletionAsync(
            userId: "user456",
            memoryStoreName: "test-store",
            auditMessage: "Memory deletion failed - connection issue",
            wasSuccessful: false,
            errorMessage: "Connection timeout");

        // Assert
        var history = await repository.GetUserDeletionHistoryAsync("user456");
        Assert.NotEmpty(history);
        var entry = history.First();
        Assert.False(entry.WasSuccessful);
        Assert.Equal("Connection timeout", entry.ErrorMessage);
    }

    [Fact(Skip = "Integration test - requires PostgreSQL")]
    public async Task GetUserDeletionHistoryAsync_ReturnsEntriesInDescendingOrder()
    {
        // Arrange
        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? throw new InvalidOperationException("POSTGRES_CONNECTION_STRING not configured");
        var options = new PostgresConversationOptions { ConnectionString = connectionString };
        var repository = new PostgresMemoryAuditRepository(options, NullLogger<PostgresMemoryAuditRepository>.Instance);
        var userId = $"user-{Guid.NewGuid()}";

        // Act - log multiple deletions
        await repository.LogMemoryDeletionAsync(userId, "store1", "First deletion attempt", true);
        await Task.Delay(10); // Small delay to ensure different timestamps
        await repository.LogMemoryDeletionAsync(userId, "store2", "Second deletion attempt", true);
        await Task.Delay(10);
        await repository.LogMemoryDeletionAsync(userId, "store3", "Third deletion failed", false, "Test error");

        var history = await repository.GetUserDeletionHistoryAsync(userId);

        // Assert - should be in reverse chronological order
        Assert.Equal(3, history.Count);
        Assert.Equal("store3", history[0].MemoryStoreName); // Most recent
        Assert.Equal("store2", history[1].MemoryStoreName);
        Assert.Equal("store1", history[2].MemoryStoreName); // Oldest
    }

    [Fact]
    public void Constructor_ThrowsWhenConnectionStringIsEmpty()
    {
        // Arrange & Act & Assert
        var options = new PostgresConversationOptions { ConnectionString = "" };
        var exception = Assert.Throws<InvalidOperationException>(
            () => new PostgresMemoryAuditRepository(options, NullLogger<PostgresMemoryAuditRepository>.Instance));
        Assert.Contains("PostgreSQL connection string is empty", exception.Message);
    }

    [Fact]
    public void Constructor_ThrowsWhenConnectionStringIsNull()
    {
        // Arrange & Act & Assert
        var options = new PostgresConversationOptions { ConnectionString = null! };
        var exception = Assert.Throws<InvalidOperationException>(
            () => new PostgresMemoryAuditRepository(options, NullLogger<PostgresMemoryAuditRepository>.Instance));
        Assert.Contains("PostgreSQL connection string is empty", exception.Message);
    }
}
