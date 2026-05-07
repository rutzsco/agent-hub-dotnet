using AgentHub.API.Agents;
using Azure.AI.Projects;
using Azure.AI.Projects.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using System.ClientModel.Primitives;

#pragma warning disable AAIP001
#pragma warning disable OPENAI001

namespace AgentHub.Tests;

public class MemoryAuditServiceTests
{
    // ----- helpers -----

    private static MemoryInspectResult InspectWith(
        IEnumerable<MemorySearchItem> searchItems,
        string? capturedQuery = null,
        string userId = "user1",
        string? topic = null)
    {
        string? recorded = null;
        var auditService = new MemoryAuditService(
            searchMemories: (scope, query, ct) =>
            {
                recorded = query;
                var response = AzureAIProjectsModelFactory.MemoryStoreSearchResponse(
                    "search-1", searchItems, usage: null);
                return Task.FromResult(response);
            },
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(userId, "store", isDeleted: true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        return auditService.InspectAsync(userId, topic, default).GetAwaiter().GetResult();
    }

    private static MemorySearchItem MakeSearchItem(string content, string scope = "user1")
    {
        var json = $@"{{""memory_item"":{{""id"":""id-1"",""updated_at"":1704067200,""scope"":""{scope}"",""content"":""{content}"",""kind"":""user_profile""}}}}";
        return ModelReaderWriter.Read<MemorySearchItem>(BinaryData.FromString(json))!;
    }

    // ----- InspectAsync -----

    [Fact]
    public async Task InspectAsync_ReturnsMemoriesFromResponse()
    {
        var items = new[] { MakeSearchItem("User likes hiking"), MakeSearchItem("User is in Seattle") };
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", items, null)),
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        var result = await auditService.InspectAsync("user1", null, default);

        Assert.Equal("user1", result.UserId);
        Assert.Equal(2, result.Memories.Length);
        Assert.Contains("User likes hiking", result.Memories);
        Assert.Contains("User is in Seattle", result.Memories);
    }

    [Fact]
    public async Task InspectAsync_EmptyTopic_UsesDefaultQuery()
    {
        string? capturedQuery = null;
        var auditService = new MemoryAuditService(
            searchMemories: (_, query, _) =>
            {
                capturedQuery = query;
                return Task.FromResult(AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null));
            },
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        await auditService.InspectAsync("user1", topic: null, default);

        Assert.Equal("user context preferences history", capturedQuery);
    }

    [Fact]
    public async Task InspectAsync_WithTopic_UsesTopicAsQuery()
    {
        string? capturedQuery = null;
        var auditService = new MemoryAuditService(
            searchMemories: (_, query, _) =>
            {
                capturedQuery = query;
                return Task.FromResult(AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null));
            },
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        await auditService.InspectAsync("user1", topic: "project preferences", default);

        Assert.Equal("project preferences", capturedQuery);
    }

    [Fact]
    public async Task InspectAsync_FiltersNullAndWhitespaceContent()
    {
        var items = new[]
        {
            MakeSearchItem("Valid memory"),
            MakeSearchItem("   "),
        };
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", items, null)),
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        var result = await auditService.InspectAsync("user1", null, default);

        Assert.Single(result.Memories);
        Assert.Equal("Valid memory", result.Memories[0]);
    }

    [Fact]
    public async Task InspectAsync_NoMemories_ReturnsEmptyArray()
    {
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("user1", "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        var result = await auditService.InspectAsync("user1", null, default);

        Assert.Empty(result.Memories);
    }

    [Fact]
    public async Task InspectAsync_PassesCorrectScopeToSearch()
    {
        string? capturedScope = null;
        var auditService = new MemoryAuditService(
            searchMemories: (scope, _, _) =>
            {
                capturedScope = scope;
                return Task.FromResult(AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null));
            },
            deleteScope: (_, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse("alice", "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        await auditService.InspectAsync("alice", null, default);

        Assert.Equal("alice", capturedScope);
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_CallsFoundryWithCorrectScope()
    {
        string? capturedScope = null;
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) =>
            {
                capturedScope = scope;
                return Task.FromResult(
                    AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", true));
            },
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        await auditService.DeleteAsync("bob", default);

        Assert.Equal("bob", capturedScope);
    }

    [Fact]
    public async Task DeleteAsync_WhenFoundryConfirmsDelete_FoundryScopeDeletedIsTrue()
    {
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", isDeleted: true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        var result = await auditService.DeleteAsync("user1", default);

        Assert.True(result.FoundryScopeDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenFoundryReturnsNotDeleted_FoundryScopeDeletedIsFalse()
    {
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", isDeleted: false)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        var result = await auditService.DeleteAsync("user1", default);

        Assert.False(result.FoundryScopeDeleted);
    }

    [Fact]
    public async Task DeleteAsync_KnownUser_LocalCacheClearedIsTrue()
    {
        var sessionCache = new FoundryMemorySessionCache(NullLogger.Instance);
        var operationCache = new FoundryMemoryOperationCache();

        // Seed operation cache so ClearUser has something to remove
        operationCache.RememberSearchId("user1", "search-1");
        operationCache.RememberUpdateId("user1", "update-1");

        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", true)),
            sessionCache: sessionCache,
            operationCache: operationCache,
            logger: NullLogger.Instance);

        var result = await auditService.DeleteAsync("user1", default);

        Assert.True(result.LocalCacheCleared);
    }

    [Fact]
    public async Task DeleteAsync_UnknownUser_LocalCacheClearedIsFalse()
    {
        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: new FoundryMemoryOperationCache(),
            logger: NullLogger.Instance);

        var result = await auditService.DeleteAsync("unknown-user", default);

        Assert.False(result.LocalCacheCleared);
    }

    [Fact]
    public async Task DeleteAsync_EvictsOperationCacheEntries()
    {
        var operationCache = new FoundryMemoryOperationCache();
        operationCache.RememberSearchId("user1", "search-123");
        operationCache.RememberUpdateId("user1", "update-456");

        var auditService = new MemoryAuditService(
            searchMemories: (_, _, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreSearchResponse("s1", [], null)),
            deleteScope: (scope, _) => Task.FromResult(
                AzureAIProjectsModelFactory.MemoryStoreDeleteScopeResponse(scope, "store", true)),
            sessionCache: new FoundryMemorySessionCache(NullLogger.Instance),
            operationCache: operationCache,
            logger: NullLogger.Instance);

        await auditService.DeleteAsync("user1", default);

        Assert.Null(operationCache.GetPreviousSearchId("user1"));
        Assert.Null(operationCache.GetPreviousUpdateId("user1"));
    }
}
