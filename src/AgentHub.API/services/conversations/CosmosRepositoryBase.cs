using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace AgentHub.API.services.conversations;

/// <summary>
/// Base class for Cosmos DB repositories. Handles database/container creation and caching.
/// Caller provides container name and partition key path.
/// </summary>
public abstract class CosmosRepositoryBase
{
    protected readonly CosmosClient Client;
    protected readonly string DatabaseName;
    protected readonly string ContainerName;
    protected readonly string PartitionKeyPath;
    protected readonly ILogger Logger;
    
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    protected Container? Container;

    protected CosmosRepositoryBase(
        CosmosOptions options,
        string containerName,
        string partitionKeyPath,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.AccountEndpoint))
            throw new InvalidOperationException(
                "Cosmos DB account endpoint is empty. Verify AgentHub:Cosmos:AccountEndpoint or COSMOS_ACCOUNT_ENDPOINT.");

        if (string.IsNullOrWhiteSpace(options.DatabaseName))
            throw new InvalidOperationException(
                "Cosmos DB database name is empty. Verify AgentHub:Cosmos:DatabaseName or COSMOS_DATABASE_NAME.");

        if (string.IsNullOrWhiteSpace(containerName))
            throw new InvalidOperationException($"Container name for '{partitionKeyPath}' is empty.");

        Client = new CosmosClient(options.AccountEndpoint, new DefaultAzureCredential());
        DatabaseName = options.DatabaseName;
        ContainerName = containerName;
        PartitionKeyPath = partitionKeyPath;
        Logger = logger;
    }

    /// <summary>
    /// Gets the container, creating it if it doesn't exist.
    /// Caches the container for subsequent calls.
    /// </summary>
    protected async Task<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        if (Container is not null)
            return Container;

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (Container is not null)
                return Container;

            Logger.LogInformation("Ensuring Cosmos DB container '{Container}' exists", ContainerName);

            var database = await Client.CreateDatabaseIfNotExistsAsync(DatabaseName, cancellationToken: cancellationToken);
            var response = await database.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(ContainerName, PartitionKeyPath),
                cancellationToken: cancellationToken);

            Container = response.Container;
            Logger.LogInformation("Cosmos DB container '{Container}' ready", ContainerName);
            return Container;
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}
