using AgentHub.API.Agents;
using AgentHub.API.services.conversations;
using AgentHub.API.services.session;
using AgentHub.API.Services.Memory;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace AgentHub.API;

public static class AgentHubServiceCollectionExtensions
{
    public static IServiceCollection AddAgentHubServices(this IServiceCollection services, Settings settings)
    {
        services.AddSingleton(settings);
        services.AddAgents(settings);
        services.AddConversationServices(settings);

        return services;
    }

    private static IServiceCollection AddAgents(this IServiceCollection services, Settings settings)
    {
        services.AddKeyedSingleton<AIAgent>("demo", (serviceProvider, _) =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.AgentRegistration");
            logger.LogInformation("Registering demo agent instance using direct AI project model inference.");
            return DemoAzureOpenAIAgent.Create(settings);
        });

#pragma warning disable OPENAI001 // FoundryAgent is experimental
        services.AddKeyedSingleton<FoundryAgent>("foundry-demo", (serviceProvider, _) =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.FoundryAgentRegistration");
            logger.LogInformation("Registering Foundry demo agent instance.");
            return FoundryDemoAgent.CreateAsync(settings, logger).GetAwaiter().GetResult();
        });
#pragma warning restore OPENAI001

        services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.FoundryMemoryAgentRegistration");
            logger.LogInformation("Registering Foundry memory agent with Foundry-managed memory.");
            return FoundryMemoryAgent.CreateAsync(settings, logger).GetAwaiter().GetResult();
        });

        services.AddSingleton(serviceProvider =>
        {
            var memoryContext = serviceProvider.GetRequiredService<FoundryMemoryContext>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.MemoryAuditService");
            return new MemoryAuditService(memoryContext, logger);
        });

        services.AddSingleton<IMemoryAuditRepository>(serviceProvider =>
        {
            var cosmosOptions = serviceProvider.GetRequiredService<CosmosOptions>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<CosmosMemoryAuditRepository>();
            return new CosmosMemoryAuditRepository(cosmosOptions, logger);
        });

        return services;
    }

    private static IServiceCollection AddConversationServices(this IServiceCollection services, Settings settings)
    {
        var cosmosOptions = new CosmosOptions
        {
            AccountEndpoint = settings.CosmosAccountEndpoint ?? string.Empty,
            DatabaseName = settings.CosmosDatabaseName ?? "agent-hub",
            ConversationContainerName = settings.CosmosConversationContainerName,
            MemoryAuditContainerName = settings.CosmosMemoryAuditContainerName
        };
        services.AddSingleton(cosmosOptions);

        if (!string.IsNullOrWhiteSpace(settings.CosmosAccountEndpoint))
        {
            services.AddSingleton<IConversationHistoryRepository, CosmosConversationHistoryRepository>();
        }
        else
        {
            services.AddSingleton<IConversationHistoryRepository, InMemoryConversationHistoryRepository>();
        }

        services.AddSingleton<IConversationSessionManager, ConversationSessionManager>();

        return services;
    }
}
