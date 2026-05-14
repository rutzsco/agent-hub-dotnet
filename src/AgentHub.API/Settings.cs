using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AgentHub.API;

public class Settings
{
    public Uri? AzureAIProjectEndpoint { get; init; }
    public Uri? AzureOpenAIEndpoint { get; init; }
    public string AzureAIModelDeploymentName { get; init; } = "gpt-4o-mini";
    public string? AzureAIApiKey { get; init; }
    public string? ApimSubscriptionKey { get; init; }
    public string? FoundryAgentName { get; init; }
    public string MemoryStoreName { get; init; } = "agent-hub-memory";
    public string MemoryEmbeddingModel { get; init; } = "text-embedding-3-small";
    public string? PostgresConnectionString { get; init; }

    public static Settings Load(IConfiguration configuration)
    {
        var agentHubSection = configuration.GetSection("AgentHub");

        var endpoint = GetOptionalValue(
            agentHubSection["AzureAIProjectEndpoint"],
            configuration["AZURE_AI_PROJECT_ENDPOINT"]);

        var modelDeploymentName = GetOptionalValue(
            agentHubSection["AzureAIModelDeploymentName"],
            configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"])
            ?? "gpt-4o-mini";

        var azureOpenAIEndpoint = GetOptionalValue(
            agentHubSection["AzureOpenAIEndpoint"],
            configuration["AZURE_OPENAI_ENDPOINT"]);

        var azureAIApiKey = GetOptionalValue(
            agentHubSection["AzureAIApiKey"],
            configuration["AZURE_AI_API_KEY"],
            configuration["AZURE_OPENAI_API_KEY"]);

        var apimSubscriptionKey = GetOptionalValue(
            agentHubSection["ApimSubscriptionKey"],
            configuration["APIM_SUBSCRIPTION_KEY"]);

        var foundryAgentName = GetOptionalValue(
            agentHubSection["FoundryAgentName"],
            configuration["AZURE_AI_FOUNDRY_AGENT_NAME"]);

        var memoryStoreName = GetOptionalValue(
            agentHubSection["MemoryStoreName"],
            configuration["AZURE_AI_MEMORY_STORE_NAME"])
            ?? "agent-hub-memory";

        var memoryEmbeddingModel = GetOptionalValue(
            agentHubSection["MemoryEmbeddingModel"],
            configuration["AZURE_AI_MEMORY_EMBEDDING_MODEL"])
            ?? "text-embedding-3-small";

        var postgresConnectionString = LoadPostgresConnectionString(configuration);

        return new Settings
        {
            AzureAIProjectEndpoint = endpoint is null ? null : new Uri(endpoint),
            AzureOpenAIEndpoint = azureOpenAIEndpoint is null ? null : new Uri(azureOpenAIEndpoint),
            AzureAIModelDeploymentName = modelDeploymentName,
            AzureAIApiKey = azureAIApiKey,
            ApimSubscriptionKey = apimSubscriptionKey,
            FoundryAgentName = foundryAgentName,
            MemoryStoreName = memoryStoreName,
            MemoryEmbeddingModel = memoryEmbeddingModel,
            PostgresConnectionString = postgresConnectionString
        };
    }

    private static string? GetOptionalValue(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public Uri RequireAzureAIProjectEndpoint()
    {
        return AzureAIProjectEndpoint
            ?? throw new InvalidOperationException(
                "Foundry agents are not configured. Set AgentHub:AzureAIProjectEndpoint or AZURE_AI_PROJECT_ENDPOINT to enable Foundry-backed endpoints.");
    }

    public Uri RequireAzureOpenAIEndpoint()
    {
        return AzureOpenAIEndpoint
            ?? AzureAIProjectEndpoint
            ?? throw new InvalidOperationException(
                "Azure OpenAI endpoint is not configured. Set AgentHub:AzureOpenAIEndpoint, AZURE_OPENAI_ENDPOINT, AgentHub:AzureAIProjectEndpoint, or AZURE_AI_PROJECT_ENDPOINT.");
    }

    private static string? LoadPostgresConnectionString(IConfiguration configuration)
    {
        var postgresSection = configuration.GetSection("AgentHub:Postgres");

        var explicitConnectionString = GetOptionalValue(
            postgresSection["ConnectionString"],
            configuration["POSTGRES_CONNECTION_STRING"],
            configuration["POSTGRES_URL"]);

        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var host = GetOptionalValue(postgresSection["Host"], configuration["POSTGRES_HOST"]);
        var database = GetOptionalValue(postgresSection["Database"], configuration["POSTGRES_DATABASE"]);
        var username = GetOptionalValue(postgresSection["Username"], configuration["POSTGRES_USERNAME"]);
        var password = GetOptionalValue(postgresSection["Password"], configuration["POSTGRES_PASSWORD"]);
        var port = GetOptionalValue(postgresSection["Port"], configuration["POSTGRES_PORT"]) ?? "5432";
        var sslMode = GetOptionalValue(postgresSection["SslMode"], configuration["POSTGRES_SSL_MODE"]) ?? "Prefer";

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var parsedPort) ? parsedPort : 5432,
            Database = database,
            Username = username,
            Password = password,
            SslMode = Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var parsedSsl) ? parsedSsl : SslMode.Prefer
        };

        return builder.ConnectionString;
    }
}
