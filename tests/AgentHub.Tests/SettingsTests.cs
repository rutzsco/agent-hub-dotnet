using AgentHub.API;
using Microsoft.Extensions.Configuration;

namespace AgentHub.Tests;

public class SettingsTests
{
    [Fact]
    public void Load_AllValues_CreatesSettings()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1",
            ["AgentHub:AzureOpenAIEndpoint"] = "https://test-openai.openai.azure.com/",
            ["AgentHub:AzureAIModelDeploymentName"] = "gpt-4o",
            ["AgentHub:FoundryAgentName"] = "test-agent",
            ["AgentHub:MemoryStoreName"] = "test-memory",
            ["AgentHub:MemoryEmbeddingModel"] = "text-embedding-ada-002",
            ["AgentHub:Cosmos:AccountEndpoint"] = "https://example.documents.azure.com:443/",
            ["AgentHub:Cosmos:DatabaseName"] = "agent-hub-db",
            ["AgentHub:Cosmos:ConversationContainerName"] = "conversation-history",
            ["AgentHub:Cosmos:MemoryAuditContainerName"] = "memory-audit-log"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://test.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://test-openai.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("gpt-4o", settings.AzureAIModelDeploymentName);
        Assert.Equal("test-agent", settings.FoundryAgentName);
        Assert.Equal("test-memory", settings.MemoryStoreName);
        Assert.Equal("text-embedding-ada-002", settings.MemoryEmbeddingModel);
        Assert.Equal("https://example.documents.azure.com:443/", settings.CosmosAccountEndpoint);
        Assert.Equal("agent-hub-db", settings.CosmosDatabaseName);
        Assert.Equal("conversation-history", settings.CosmosConversationContainerName);
        Assert.Equal("memory-audit-log", settings.CosmosMemoryAuditContainerName);
    }

    [Fact]
    public void Load_UsesDefaults_WhenOptionalValuesOmitted()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1"
        });

        var settings = Settings.Load(config);

        Assert.Null(settings.AzureOpenAIEndpoint);
        Assert.Equal("gpt-4o-mini", settings.AzureAIModelDeploymentName);
        Assert.Null(settings.FoundryAgentName);
        Assert.Equal("agent-hub-memory", settings.MemoryStoreName);
        Assert.Equal("text-embedding-3-small", settings.MemoryEmbeddingModel);
        Assert.Null(settings.CosmosAccountEndpoint);
        Assert.Null(settings.CosmosDatabaseName);
        Assert.Equal("conversation-messages", settings.CosmosConversationContainerName);
        Assert.Equal("memory-audit", settings.CosmosMemoryAuditContainerName);
    }

    [Fact]
    public void Load_FromEnvironmentVariables()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AZURE_AI_PROJECT_ENDPOINT"] = "https://env.services.ai.azure.com/api/projects/proj1",
            ["AZURE_OPENAI_ENDPOINT"] = "https://env-openai.openai.azure.com/",
            ["AZURE_AI_MODEL_DEPLOYMENT_NAME"] = "gpt-4o-env",
            ["AZURE_AI_FOUNDRY_AGENT_NAME"] = "env-agent",
            ["AZURE_AI_MEMORY_STORE_NAME"] = "env-memory",
            ["AZURE_AI_MEMORY_EMBEDDING_MODEL"] = "env-embed",
            ["COSMOS_ACCOUNT_ENDPOINT"] = "https://env.documents.azure.com:443/",
            ["COSMOS_DATABASE_NAME"] = "env-db",
            ["COSMOS_CONVERSATION_CONTAINER_NAME"] = "env-conversations",
            ["COSMOS_MEMORY_AUDIT_CONTAINER_NAME"] = "env-audit"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://env.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://env-openai.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("gpt-4o-env", settings.AzureAIModelDeploymentName);
        Assert.Equal("env-agent", settings.FoundryAgentName);
        Assert.Equal("env-memory", settings.MemoryStoreName);
        Assert.Equal("env-embed", settings.MemoryEmbeddingModel);
        Assert.Equal("https://env.documents.azure.com:443/", settings.CosmosAccountEndpoint);
        Assert.Equal("env-db", settings.CosmosDatabaseName);
        Assert.Equal("env-conversations", settings.CosmosConversationContainerName);
        Assert.Equal("env-audit", settings.CosmosMemoryAuditContainerName);
    }

    [Fact]
    public void Load_ThrowsWhenEndpointMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:Cosmos:DatabaseName"] = "agent-hub-db"
        });

        Assert.Throws<InvalidOperationException>(() => Settings.Load(config));
    }

    [Fact]
    public void Load_CosmosIsNull_WhenNotConfigured()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1"
        });

        var settings = Settings.Load(config);

        Assert.Null(settings.CosmosAccountEndpoint);
        Assert.Null(settings.CosmosDatabaseName);
    }

    [Fact]
    public void Load_SectionConfigTakesPrecedenceOverEnvVars()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://section.services.ai.azure.com/api/projects/proj1",
            ["AZURE_AI_PROJECT_ENDPOINT"] = "https://env.services.ai.azure.com/api/projects/proj1",
            ["AgentHub:AzureOpenAIEndpoint"] = "https://section-openai.openai.azure.com/",
            ["AZURE_OPENAI_ENDPOINT"] = "https://env-openai.openai.azure.com/",
            ["AgentHub:Cosmos:AccountEndpoint"] = "https://section.documents.azure.com:443/",
            ["COSMOS_ACCOUNT_ENDPOINT"] = "https://env.documents.azure.com:443/",
            ["AgentHub:Cosmos:DatabaseName"] = "section-db",
            ["COSMOS_DATABASE_NAME"] = "env-db"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://section.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
    Assert.Equal(new Uri("https://section-openai.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("https://section.documents.azure.com:443/", settings.CosmosAccountEndpoint);
        Assert.Equal("section-db", settings.CosmosDatabaseName);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
