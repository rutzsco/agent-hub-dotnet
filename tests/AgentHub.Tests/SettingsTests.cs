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
            ["AgentHub:AzureAIApiKey"] = "section-key",
            ["AgentHub:ApimSubscriptionKey"] = "section-apim-key",
            ["AgentHub:FoundryAgentName"] = "test-agent",
            ["AgentHub:MemoryStoreName"] = "test-memory",
            ["AgentHub:MemoryEmbeddingModel"] = "text-embedding-ada-002",
            ["AgentHub:Cosmos:AccountEndpoint"] = "https://example.documents.azure.com:443/",
            ["AgentHub:Cosmos:DatabaseName"] = "agent-hub-db",
            ["AgentHub:Cosmos:ConversationContainerName"] = "conversation-history",
            ["AgentHub:Cosmos:MemoryAuditContainerName"] = "memory-audit-log",
            ["AgentHub:Cosmos:KnowledgeBaseContainerName"] = "kb-chunks",
            ["AgentHub:KnowledgeBase:BlobContainerUri"] = "https://storage.blob.core.windows.net/kb",
            ["AgentHub:KnowledgeBase:BlobPrefix"] = "internal_docs/",
            ["AgentHub:KnowledgeBase:DocumentIntelligenceEndpoint"] = "https://docs.cognitiveservices.azure.com/",
            ["AgentHub:KnowledgeBase:ChunkMaxCharacters"] = "2500",
            ["AgentHub:KnowledgeBase:ChunkOverlapCharacters"] = "250",
            ["AgentHub:KnowledgeBase:DefaultMaxFiles"] = "3",
            ["AgentHub:KnowledgeBase:MaxChunksPerDocument"] = "80"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://test.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://test-openai.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("gpt-4o", settings.AzureAIModelDeploymentName);
        Assert.Equal("section-key", settings.AzureAIApiKey);
        Assert.Equal("section-apim-key", settings.ApimSubscriptionKey);
        Assert.Equal("test-agent", settings.FoundryAgentName);
        Assert.Equal("test-memory", settings.MemoryStoreName);
        Assert.Equal("text-embedding-ada-002", settings.MemoryEmbeddingModel);
        Assert.Equal("https://example.documents.azure.com:443/", settings.CosmosAccountEndpoint);
        Assert.Equal("agent-hub-db", settings.CosmosDatabaseName);
        Assert.Equal("conversation-history", settings.CosmosConversationContainerName);
        Assert.Equal("memory-audit-log", settings.CosmosMemoryAuditContainerName);
        Assert.Equal("kb-chunks", settings.CosmosKnowledgeBaseContainerName);
        Assert.Equal(new Uri("https://storage.blob.core.windows.net/kb"), settings.KnowledgeBaseBlobContainerUri);
        Assert.Equal("internal_docs/", settings.KnowledgeBaseBlobPrefix);
        Assert.Equal(new Uri("https://docs.cognitiveservices.azure.com/"), settings.KnowledgeBaseDocumentIntelligenceEndpoint);
        Assert.Equal(2500, settings.KnowledgeBaseChunkMaxCharacters);
        Assert.Equal(250, settings.KnowledgeBaseChunkOverlapCharacters);
        Assert.Equal(3, settings.KnowledgeBaseDefaultMaxFiles);
        Assert.Equal(80, settings.KnowledgeBaseMaxChunksPerDocument);
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
        Assert.Null(settings.AzureAIApiKey);
        Assert.Null(settings.ApimSubscriptionKey);
        Assert.Null(settings.FoundryAgentName);
        Assert.Equal("agent-hub-memory", settings.MemoryStoreName);
        Assert.Equal("text-embedding-3-small", settings.MemoryEmbeddingModel);
        Assert.Null(settings.CosmosAccountEndpoint);
        Assert.Null(settings.CosmosDatabaseName);
        Assert.Equal("conversation-messages", settings.CosmosConversationContainerName);
        Assert.Equal("memory-audit", settings.CosmosMemoryAuditContainerName);
        Assert.Equal("knowledge-base-chunks", settings.CosmosKnowledgeBaseContainerName);
        Assert.Null(settings.KnowledgeBaseBlobContainerUri);
        Assert.Null(settings.KnowledgeBaseBlobPrefix);
        Assert.Null(settings.KnowledgeBaseDocumentIntelligenceEndpoint);
        Assert.Equal(3500, settings.KnowledgeBaseChunkMaxCharacters);
        Assert.Equal(400, settings.KnowledgeBaseChunkOverlapCharacters);
        Assert.Equal(10, settings.KnowledgeBaseDefaultMaxFiles);
        Assert.Equal(500, settings.KnowledgeBaseMaxChunksPerDocument);
    }

    [Fact]
    public void Load_BlankSectionValues_FallBackToEnvironmentVariablesAndDefaults()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "",
            ["AZURE_AI_PROJECT_ENDPOINT"] = "https://env.services.ai.azure.com/api/projects/proj1",
            ["AgentHub:AzureOpenAIEndpoint"] = "",
            ["AZURE_OPENAI_ENDPOINT"] = "https://env-openai.openai.azure.com/",
            ["AgentHub:AzureAIModelDeploymentName"] = "",
            ["AZURE_AI_MODEL_DEPLOYMENT_NAME"] = "gpt-4o-env",
            ["AgentHub:FoundryAgentName"] = "",
            ["AZURE_AI_FOUNDRY_AGENT_NAME"] = "env-agent",
            ["AgentHub:MemoryStoreName"] = "",
            ["AgentHub:MemoryEmbeddingModel"] = "",
            ["AgentHub:Cosmos:AccountEndpoint"] = "",
            ["COSMOS_ACCOUNT_ENDPOINT"] = "https://env.documents.azure.com:443/"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://env.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://env-openai.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("gpt-4o-env", settings.AzureAIModelDeploymentName);
        Assert.Equal("env-agent", settings.FoundryAgentName);
        Assert.Equal("agent-hub-memory", settings.MemoryStoreName);
        Assert.Equal("text-embedding-3-small", settings.MemoryEmbeddingModel);
        Assert.Equal("https://env.documents.azure.com:443/", settings.CosmosAccountEndpoint);
    }

    [Fact]
    public void Load_KnowledgeBase_FromEnvironmentVariables()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AZURE_AI_PROJECT_ENDPOINT"] = "https://env.services.ai.azure.com/api/projects/proj1",
            ["COSMOS_KNOWLEDGE_BASE_CONTAINER_NAME"] = "env-kb",
            ["KNOWLEDGE_BASE_BLOB_CONTAINER_URI"] = "https://envstorage.blob.core.windows.net/kb",
            ["KNOWLEDGE_BASE_BLOB_PREFIX"] = "manuals/",
            ["DOCUMENT_INTELLIGENCE_ENDPOINT"] = "https://env-docs.cognitiveservices.azure.com/",
            ["KNOWLEDGE_BASE_CHUNK_MAX_CHARACTERS"] = "1200",
            ["KNOWLEDGE_BASE_CHUNK_OVERLAP_CHARACTERS"] = "100",
            ["KNOWLEDGE_BASE_DEFAULT_MAX_FILES"] = "4",
            ["KNOWLEDGE_BASE_MAX_CHUNKS_PER_DOCUMENT"] = "40"
        });

        var settings = Settings.Load(config);

        Assert.Equal("env-kb", settings.CosmosKnowledgeBaseContainerName);
        Assert.Equal(new Uri("https://envstorage.blob.core.windows.net/kb"), settings.KnowledgeBaseBlobContainerUri);
        Assert.Equal("manuals/", settings.KnowledgeBaseBlobPrefix);
        Assert.Equal(new Uri("https://env-docs.cognitiveservices.azure.com/"), settings.KnowledgeBaseDocumentIntelligenceEndpoint);
        Assert.Equal(1200, settings.KnowledgeBaseChunkMaxCharacters);
        Assert.Equal(100, settings.KnowledgeBaseChunkOverlapCharacters);
        Assert.Equal(4, settings.KnowledgeBaseDefaultMaxFiles);
        Assert.Equal(40, settings.KnowledgeBaseMaxChunksPerDocument);
    }

    [Fact]
    public void Load_AllowsMissingFoundryEndpoint()
    {
        var settings = Settings.Load(BuildConfig(new Dictionary<string, string?>()));

        Assert.Null(settings.AzureAIProjectEndpoint);
        Assert.Throws<InvalidOperationException>(settings.RequireAzureAIProjectEndpoint);
    }

    [Fact]
    public void RequireAzureOpenAIEndpoint_UsesAzureOpenAIEndpointFirst()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureOpenAIEndpoint"] = "https://test-openai.openai.azure.com/",
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://test-openai.openai.azure.com/"), settings.RequireAzureOpenAIEndpoint());
    }

    [Fact]
    public void RequireAzureOpenAIEndpoint_FallsBackToAzureAIProjectEndpoint()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://test.services.ai.azure.com/api/projects/proj1"), settings.RequireAzureOpenAIEndpoint());
    }

    [Fact]
    public void Load_FromEnvironmentVariables()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AZURE_AI_PROJECT_ENDPOINT"] = "https://env.services.ai.azure.com/api/projects/proj1",
            ["AZURE_OPENAI_ENDPOINT"] = "https://env-openai.openai.azure.com/",
            ["AZURE_AI_MODEL_DEPLOYMENT_NAME"] = "gpt-4o-env",
            ["AZURE_AI_API_KEY"] = "env-key",
            ["APIM_SUBSCRIPTION_KEY"] = "env-apim-key",
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
        Assert.Equal("env-key", settings.AzureAIApiKey);
        Assert.Equal("env-apim-key", settings.ApimSubscriptionKey);
        Assert.Equal("env-agent", settings.FoundryAgentName);
        Assert.Equal("env-memory", settings.MemoryStoreName);
        Assert.Equal("env-embed", settings.MemoryEmbeddingModel);
        Assert.Equal("https://env.documents.azure.com:443/", settings.CosmosAccountEndpoint);
        Assert.Equal("env-db", settings.CosmosDatabaseName);
        Assert.Equal("env-conversations", settings.CosmosConversationContainerName);
        Assert.Equal("env-audit", settings.CosmosMemoryAuditContainerName);
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
            ["AgentHub:AzureAIApiKey"] = "section-key",
            ["AZURE_AI_API_KEY"] = "env-key",
            ["AgentHub:Cosmos:AccountEndpoint"] = "https://section.documents.azure.com:443/",
            ["COSMOS_ACCOUNT_ENDPOINT"] = "https://env.documents.azure.com:443/",
            ["AgentHub:Cosmos:DatabaseName"] = "section-db",
            ["COSMOS_DATABASE_NAME"] = "env-db",
            ["AgentHub:ApimSubscriptionKey"] = "section-apim-key",
            ["APIM_SUBSCRIPTION_KEY"] = "env-apim-key"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://section.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://section-openai.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("section-key", settings.AzureAIApiKey);
        Assert.Equal("https://section.documents.azure.com:443/", settings.CosmosAccountEndpoint);
        Assert.Equal("section-db", settings.CosmosDatabaseName);
        Assert.Equal("section-apim-key", settings.ApimSubscriptionKey);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}