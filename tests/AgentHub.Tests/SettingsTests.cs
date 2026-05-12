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
            ["AgentHub:AzureOpenAIEndpoint"] = "https://test.openai.azure.com/",
            ["AgentHub:AzureAIModelDeploymentName"] = "gpt-4o",
            ["AgentHub:AzureAIApiKey"] = "section-key",
            ["AgentHub:FoundryAgentName"] = "test-agent",
            ["AgentHub:MemoryStoreName"] = "test-memory",
            ["AgentHub:MemoryEmbeddingModel"] = "text-embedding-ada-002",
            ["AgentHub:Postgres:ConnectionString"] = "Host=localhost;Database=test"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://test.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://test.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("gpt-4o", settings.AzureAIModelDeploymentName);
        Assert.Equal("section-key", settings.AzureAIApiKey);
        Assert.Equal("test-agent", settings.FoundryAgentName);
        Assert.Equal("test-memory", settings.MemoryStoreName);
        Assert.Equal("text-embedding-ada-002", settings.MemoryEmbeddingModel);
        Assert.Equal("Host=localhost;Database=test", settings.PostgresConnectionString);
    }

    [Fact]
    public void Load_UsesDefaults_WhenOptionalValuesOmitted()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1",
            ["AgentHub:Postgres:ConnectionString"] = "Host=localhost;Database=test"
        });

        var settings = Settings.Load(config);

        Assert.Equal("gpt-4o-mini", settings.AzureAIModelDeploymentName);
        Assert.Null(settings.AzureOpenAIEndpoint);
        Assert.Null(settings.AzureAIApiKey);
        Assert.Null(settings.FoundryAgentName);
        Assert.Equal("agent-hub-memory", settings.MemoryStoreName);
        Assert.Equal("text-embedding-3-small", settings.MemoryEmbeddingModel);
    }

    [Fact]
    public void Load_FromEnvironmentVariables()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AZURE_AI_PROJECT_ENDPOINT"] = "https://env.services.ai.azure.com/api/projects/proj1",
            ["AZURE_OPENAI_ENDPOINT"] = "https://env.openai.azure.com/",
            ["AZURE_AI_MODEL_DEPLOYMENT_NAME"] = "gpt-4o-env",
            ["AZURE_AI_API_KEY"] = "env-key",
            ["AZURE_AI_FOUNDRY_AGENT_NAME"] = "env-agent",
            ["AZURE_AI_MEMORY_STORE_NAME"] = "env-memory",
            ["AZURE_AI_MEMORY_EMBEDDING_MODEL"] = "env-embed",
            ["POSTGRES_CONNECTION_STRING"] = "Host=envhost;Database=envdb"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://env.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://env.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("gpt-4o-env", settings.AzureAIModelDeploymentName);
        Assert.Equal("env-key", settings.AzureAIApiKey);
        Assert.Equal("env-agent", settings.FoundryAgentName);
        Assert.Equal("env-memory", settings.MemoryStoreName);
        Assert.Equal("env-embed", settings.MemoryEmbeddingModel);
    }

    [Fact]
    public void Load_ThrowsWhenEndpointMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:Postgres:ConnectionString"] = "Host=localhost;Database=test"
        });

        Assert.Throws<InvalidOperationException>(() => Settings.Load(config));
    }

    [Fact]
    public void Load_PostgresIsNull_WhenNotConfigured()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1"
        });

        var settings = Settings.Load(config);

        Assert.Null(settings.PostgresConnectionString);
    }

    [Fact]
    public void Load_PostgresFromIndividualComponents()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1",
            ["AgentHub:Postgres:Host"] = "localhost",
            ["AgentHub:Postgres:Database"] = "mydb",
            ["AgentHub:Postgres:Username"] = "myuser",
            ["AgentHub:Postgres:Password"] = "mypass",
            ["AgentHub:Postgres:Port"] = "5433",
            ["AgentHub:Postgres:SslMode"] = "Require"
        });

        var settings = Settings.Load(config);

        Assert.Contains("localhost", settings.PostgresConnectionString);
        Assert.Contains("5433", settings.PostgresConnectionString);
        Assert.Contains("mydb", settings.PostgresConnectionString);
        Assert.Contains("myuser", settings.PostgresConnectionString);
    }

    [Fact]
    public void Load_PostgresFromIndividualComponents_DefaultPortAndSsl()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://test.services.ai.azure.com/api/projects/proj1",
            ["AgentHub:Postgres:Host"] = "localhost",
            ["AgentHub:Postgres:Database"] = "mydb",
            ["AgentHub:Postgres:Username"] = "myuser",
            ["AgentHub:Postgres:Password"] = "mypass"
        });

        var settings = Settings.Load(config);

        Assert.Contains("5432", settings.PostgresConnectionString);
    }

    [Fact]
    public void Load_SectionConfigTakesPrecedenceOverEnvVars()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["AgentHub:AzureAIProjectEndpoint"] = "https://section.services.ai.azure.com/api/projects/proj1",
            ["AZURE_AI_PROJECT_ENDPOINT"] = "https://env.services.ai.azure.com/api/projects/proj1",
            ["AgentHub:AzureOpenAIEndpoint"] = "https://section.openai.azure.com/",
            ["AZURE_OPENAI_ENDPOINT"] = "https://env.openai.azure.com/",
            ["AgentHub:AzureAIApiKey"] = "section-key",
            ["AZURE_AI_API_KEY"] = "env-key",
            ["AgentHub:Postgres:ConnectionString"] = "Host=localhost;Database=test"
        });

        var settings = Settings.Load(config);

        Assert.Equal(new Uri("https://section.services.ai.azure.com/api/projects/proj1"), settings.AzureAIProjectEndpoint);
        Assert.Equal(new Uri("https://section.openai.azure.com/"), settings.AzureOpenAIEndpoint);
        Assert.Equal("section-key", settings.AzureAIApiKey);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
