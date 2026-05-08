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
            logger.LogInformation("Registering Foundry memory agent with memory store and in-memory session cache.");
            logger.LogDebug("Session cache: userId-keyed, thread-safe, survives app lifetime (lost on restart). Memory store: persists in Azure beyond restarts.");
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
            var postgresOptions = serviceProvider.GetRequiredService<PostgresConversationOptions>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<PostgresMemoryAuditRepository>();
            return new PostgresMemoryAuditRepository(postgresOptions, logger);
        });

        return services;
    }

    private static IServiceCollection AddConversationServices(this IServiceCollection services, Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PostgresConnectionString))
        {
            services.AddSingleton(new PostgresConversationOptions
            {
                ConnectionString = settings.PostgresConnectionString
            });
            services.AddSingleton<IConversationHistoryRepository, PostgresConversationHistoryRepository>();
        }
        else
        {
            services.AddSingleton<IConversationHistoryRepository, InMemoryConversationHistoryRepository>();
        }

        services.AddSingleton<IConversationSessionManager, ConversationSessionManager>();

        return services;
    }
}
