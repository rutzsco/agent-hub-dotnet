using AgentHub.API.Agents;
using AgentHub.API.services.conversations;
using AgentHub.API.services.search;
using AgentHub.API.services.session;
using AgentHub.API.Services.Memory;
using AgentHub.API.Services.Skills.Validation;
using Azure.Search.Documents.Indexes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace AgentHub.API;

/// <summary>
/// Dependency-injection composition root for AgentHub.
/// Groups all service registrations (agents, conversation persistence, search, skills)
/// behind a single <see cref="AddAgentHubServices"/> extension so <c>Program.cs</c> stays lean.
/// </summary>
/// <remarks>
/// Registrations are conditional on <see cref="Settings"/> values, allowing the app to start
/// in reduced-functionality modes (e.g., without Cosmos or Azure AI Search) when those
/// endpoints are not configured.
/// </remarks>
public static class AgentHubServiceCollectionExtensions
{
    /// <summary>
    /// Registers every AgentHub service into the DI container.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="settings">Strongly-typed configuration loaded from appsettings/env vars.</param>
    /// <returns>The same <paramref name="services"/> instance to enable fluent chaining.</returns>
    public static IServiceCollection AddAgentHubServices(this IServiceCollection services, Settings settings)
    {
        // Make Settings injectable so downstream services can read configuration without touching IConfiguration.
        services.AddSingleton(settings);

        // Register feature groups. Order is not critical because all registrations are singletons resolved lazily.
        services.AddAgents(settings);
        services.AddConversationServices(settings);
        services.AddSearchServices(settings);
        services.AddSkills();

        return services;
    }

    /// <summary>
    /// Registers all AI agent instances (demo, Foundry demo, Foundry memory) plus memory-audit services.
    /// </summary>
    /// <remarks>
    /// Agents are registered as singletons because they are thread-safe clients that hold expensive
    /// SDK connections; keyed registrations let routes resolve a specific agent by name.
    /// </remarks>
    private static IServiceCollection AddAgents(this IServiceCollection services, Settings settings)
    {
        // Keyed singleton: resolved via [FromKeyedServices("demo")] or GetRequiredKeyedService<AIAgent>("demo").
        services.AddKeyedSingleton<AIAgent>("demo", (serviceProvider, _) =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.AgentRegistration");
            logger.LogInformation("Registering demo agent instance using direct AI project model inference.");
            return DemoAzureOpenAIAgent.Create(settings);
        });

#pragma warning disable OPENAI001 // FoundryAgent is an experimental API; suppress preview warning.
        services.AddKeyedSingleton<FoundryAgent>("foundry-demo", (serviceProvider, _) =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.FoundryAgentRegistration");
            logger.LogInformation("Registering Foundry demo agent instance.");
            // Block on async factory: DI container expects a synchronous result, and this runs once at startup.
            return FoundryDemoAgent.CreateAsync(settings, logger).GetAwaiter().GetResult();
        });
#pragma warning restore OPENAI001

        // Foundry memory agent — registered by concrete type (no key) since it's a single instance.
        services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.FoundryMemoryAgentRegistration");
            logger.LogInformation("Registering Foundry memory agent with Foundry-managed memory.");
            return FoundryMemoryAgent.CreateAsync(settings, logger).GetAwaiter().GetResult();
        });

        // MemoryAuditService depends on FoundryMemoryContext exposed by the FoundryMemoryAgent registration above.
        services.AddSingleton(serviceProvider =>
        {
            var memoryContext = serviceProvider.GetRequiredService<FoundryMemoryContext>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AgentHub.MemoryAuditService");
            return new MemoryAuditService(memoryContext, logger);
        });

        // Cosmos-backed audit repository for persisting memory-mutation events.
        services.AddSingleton<IMemoryAuditRepository>(serviceProvider =>
        {
            var cosmosOptions = serviceProvider.GetRequiredService<CosmosOptions>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<CosmosMemoryAuditRepository>();
            return new CosmosMemoryAuditRepository(cosmosOptions, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers conversation persistence (Cosmos DB when configured, otherwise in-memory) and session management.
    /// </summary>
    /// <remarks>
    /// Falls back to <see cref="InMemoryConversationHistoryRepository"/> when no Cosmos endpoint is set,
    /// which is convenient for local development but loses history on restart.
    /// </remarks>
    private static IServiceCollection AddConversationServices(this IServiceCollection services, Settings settings)
    {
        // Build a single CosmosOptions instance so every Cosmos-aware service sees the same configuration.
        var cosmosOptions = new CosmosOptions
        {
            AccountEndpoint = settings.CosmosAccountEndpoint ?? string.Empty,
            DatabaseName = settings.CosmosDatabaseName ?? "agent-hub",
            ConversationContainerName = settings.CosmosConversationContainerName,
            MemoryAuditContainerName = settings.CosmosMemoryAuditContainerName
        };
        services.AddSingleton(cosmosOptions);

        // Choose the conversation repository implementation based on whether Cosmos is configured.
        if (!string.IsNullOrWhiteSpace(settings.CosmosAccountEndpoint))
        {
            services.AddSingleton<IConversationHistoryRepository, CosmosConversationHistoryRepository>();
        }
        else
        {
            // Dev/test fallback — history is lost on process restart.
            services.AddSingleton<IConversationHistoryRepository, InMemoryConversationHistoryRepository>();
        }

        services.AddSingleton<IConversationSessionManager, ConversationSessionManager>();

        return services;
    }

    /// <summary>
    /// Registers reusable agent "skills" (e.g., prompt validation) that agents can compose.
    /// </summary>
    private static IServiceCollection AddSkills(this IServiceCollection services)
    {
        services.AddSingleton<PromptValidationSkill>();

        return services;
    }

    /// <summary>
    /// Conditionally registers an Azure AI Search <see cref="SearchIndexClient"/>.
    /// </summary>
    /// <remarks>
    /// When <see cref="Settings.AzureSearchEndpoint"/> is not configured, no client is registered
    /// and <c>GetService&lt;SearchIndexClient&gt;()</c> in <c>Program.cs</c> returns <c>null</c>,
    /// causing the index-creation step to be skipped gracefully.
    /// </remarks>
    private static IServiceCollection AddSearchServices(this IServiceCollection services, Settings settings)
    {
        // Skip registration entirely when the search endpoint isn't configured — the app runs without search.
        if (settings.AzureSearchEndpoint is null)
        {
            return services;
        }

        // Use Settings.CreateAzureCredential() so authentication (DefaultAzureCredential or otherwise)
        // stays consistent with the rest of the Azure SDK clients in the app.
        services.AddSingleton(_ => new SearchIndexClient(
            settings.AzureSearchEndpoint,
            settings.CreateAzureCredential()));

        return services;
    }
}
