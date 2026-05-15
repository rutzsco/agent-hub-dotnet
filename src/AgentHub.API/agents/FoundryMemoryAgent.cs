using System.ClientModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentHub.API.Services.Skills.Validation;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Projects.Memory;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;

namespace AgentHub.API.Agents;

#pragma warning disable AAIP001
#pragma warning disable MAAI001
#pragma warning disable OPENAI001

/// <summary>
/// Holds the Foundry memory agent and related clients.
/// Registered as a singleton; the route handler injects this directly.
/// No application-level session caching - Foundry manages short-term context
/// via its thread model and long-term memory via FoundryMemoryProvider.
/// </summary>
public sealed class FoundryMemoryContext
{
    public required FoundryAgent Agent { get; init; }
    public required ChatClientAgent BaseAgent { get; init; }
    public required AIProjectMemoryStores MemoryClient { get; init; }
    public required string MemoryStoreName { get; init; }
}

public static class FoundryMemoryAgent
{
    public const string DefaultAgentName = "MemoryAgent";
    private static readonly Regex UserIdPattern = new(@"^[a-zA-Z0-9][a-zA-Z0-9._%+@\-]{0,127}$", RegexOptions.Compiled);

    /// <summary>
    /// Carries the current userId through the async call stack so the FoundryMemoryProvider
    /// stateInitializer can scope memory reads/writes to the correct user.
    /// </summary>
    private static readonly AsyncLocal<string?> _currentUserId = new();

    internal const string KaiCharterSystemPrompt = """
        You are KAI - the Kaizen Charter Guide.

        Your job is to help a user fill out a structured "Event Charter" form, one
        field at a time. The user interacts with you through a UI that always tells
        you exactly which SECTION and FIELD they need help with, and what they have
        written so far. You do not need to ask them which field - trust the
        structured context provided in each request.

        # Charter structure (6 sections, weighted equally)
        1. Summary & Schedule - Problem Statement, KPI Target, KPI Actual, KPI Gap,
           KPI Trend, Process Name, Process Mapped.
        2. Metrics & Deliverables - Primary Metric, Baseline, Goal, Unit of Measure,
           Deliverables.
        3. Daily Milestones - Day 1-5 milestones for the kaizen event week.
        4. Team & On-Call - Executive Sponsor, Team Leader, Facilitator, Members,
           On-Call Primary/Secondary.
        5. Obstacles & Resources - Obstacles, Required Resources, Risks &
           Mitigations.
        6. Sustainability Metrics - Control Plan, Audit Frequency, Process Owner,
           Review Cadence, Long-Term Success Criteria.

        # Request format you will receive
        Every user message is a JSON object with this shape:

        {
          "intent": "field_help" | "section_review" | "freeform",
          "section": { "id": "...", "title": "..." },
          "field":   { "id": "...", "label": "..." } | null,
          "currentValue": "<what the user has typed in this field>" | "",
          "sectionValues": {
            "<fieldLabel>": "<value or empty string>",
            ...
          },
          "userMessage": "<optional free-form note from the user>" | ""
        }

        Treat this JSON as authoritative ground truth. If a field is empty, that
        means the user has not written anything yet. Use sectionValues to keep
        your suggestions consistent with what is already filled in elsewhere in
        the same section.

        # Frameworks to apply
        - Problem Statement -> use the TAGS framework: Target, Actual, Gap,
          Standard/Trend. Always include a quantified gap and a time horizon.
        - KPI fields -> require a unit, a numeric value, and a clear definition.
          Flag inconsistency if Target - Actual != Gap.
        - Daily Milestones -> ensure each day has a measurable deliverable and
          builds on the previous day.
        - Team & On-Call -> require named roles, not generic titles.
        - Obstacles & Resources -> each obstacle should pair with a mitigation
          and a required resource.
        - Sustainability -> require an owner, a cadence, and a measurable success
          criterion.

        # Response format
        Respond in Markdown, kept short (under ~200 words). Use this structure
        when the intent is field_help:

        **Tips for "<field label>"**
        - 3 to 5 short, specific bullets.

        **Suggested wording**
        > A single concrete example the user could paste into the field, tailored
        > to their currentValue and sectionValues.

        When the intent is section_review:

        **Section: <section title>**
        - Bulleted critique covering what is missing, what could be sharper, and
          what to write next.
        - End with one sentence naming the single most important next step.

        When the intent is freeform, answer the userMessage directly but stay on
        the topic of the current section/field.

        # Hard rules
        - Never invent numbers the user has not provided. If you suggest example
          numbers, prefix them with "Example:" so it is obvious they are
          illustrative.
        - Do not propose solutions inside the Problem Statement field - only
          describe the current state.
        - Do not output JSON. Do not echo the request back. Do not add
          meta-commentary like "Sure, here are some tips".
        - Use the user's own words and numbers from sectionValues whenever
          possible to keep tone consistent across fields.
        - If the request is ambiguous, ask exactly one clarifying question
          instead of guessing.
        - You have persistent memory scoped to this user across charters - use it
          to keep terminology, KPI definitions, and team names consistent with
          past sessions, but never reveal another user's data.
        """;

    public static async Task<FoundryMemoryContext> CreateAsync(Settings settings, ILogger logger)
    {
        logger.LogInformation(
            "Initializing Foundry memory agent. Endpoint={Endpoint}, MemoryStore={MemoryStore}, EmbeddingModel={EmbeddingModel}",
            settings.AzureAIProjectEndpoint,
            settings.MemoryStoreName,
            settings.MemoryEmbeddingModel);

        var client = new AIProjectClient(settings.AzureAIProjectEndpoint, settings.CreateAzureCredential());
        var memoryClient = client.GetAIProjectMemoryStoresClient();

        var memoryStore = await GetOrCreateMemoryStoreAsync(memoryClient, settings, logger);
        logger.LogInformation("Memory store ready. Name={MemoryStoreName}", memoryStore.Name);

        // FoundryMemoryProvider automatically retrieves relevant memories before each run
        // and persists new conversation turns after each run, scoped by userId.
        var memoryProvider = new FoundryMemoryProvider(
            client,
            settings.MemoryStoreName,
            session => new FoundryMemoryProvider.State(
                new FoundryMemoryProviderScope(_currentUserId.Value!)));

        var agentName = settings.FoundryAgentName is not null
            ? $"{settings.FoundryAgentName}-memory"
            : DefaultAgentName;

        var record = await GetOrCreateAgentAsync(client, agentName, settings, logger);
        logger.LogInformation("Foundry memory agent is ready. AgentName={AgentName}", record.Name);

        // Build server-side Foundry agent wrapper and attach FoundryMemoryProvider
        // via chatClient transform.
        var agent = (FoundryAgent)client.AsAIAgent(
            record,
            [],
            inner => CreateContextProviderChatClient(inner, memoryProvider),
            null);

        var chatAgent = (ChatClientAgent)agent.GetService(typeof(ChatClientAgent), null)!;

        return new FoundryMemoryContext
        {
            Agent = agent,
            BaseAgent = chatAgent,
            MemoryClient = memoryClient,
            MemoryStoreName = settings.MemoryStoreName
        };
    }

    public static async Task<MemoryAgentMessageResult> ProcessMessage(
        FoundryMemoryContext memoryContext,
        PromptValidationSkill validationSkill,
        string message,
        string userId,
        string? conversationId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ValidateRequest(message, userId, logger);

        // Validate prompt for safety before processing
        var validationResult = await validationSkill.ExecuteAsync(
            new PromptValidationInput { Prompt = message, UserId = userId },
            cancellationToken);

        if (!validationResult.IsValid)
        {
            logger.LogWarning(
                "Prompt validation failed for Foundry memory agent. UserId={UserId}, Rule={FailedRule}, Error={ErrorMessage}",
                userId,
                validationResult.FailedRule,
                validationResult.ErrorMessage);

            throw new ArgumentException(
                validationResult.ErrorMessage ?? "Prompt validation failed",
                nameof(message));
        }

        // Set userId so FoundryMemoryProvider.stateInitializer can scope memory to this user.
        _currentUserId.Value = userId;
        try
        {
            AgentSession session;
            if (conversationId is null)
            {
                // New conversation: create a server-side Foundry thread.
                session = await memoryContext.Agent.CreateConversationSessionAsync(cancellationToken);
                logger.LogInformation("Created new Foundry conversation session. UserId={UserId}", userId);
            }
            else
            {
                // Follow-up: resume the existing Foundry thread by its conversation ID.
                session = await memoryContext.BaseAgent.CreateSessionAsync(conversationId, cancellationToken);
                logger.LogInformation(
                    "Resumed Foundry conversation session. UserId={UserId}, ConversationId={ConversationId}",
                    userId, conversationId);
            }

            logger.LogDebug(
                "Running Foundry memory agent with session type {SessionType} and ConversationId={ConversationId}",
                session.GetType().Name,
                (session as ChatClientAgentSession)?.ConversationId);

            // FoundryMemoryProvider automatically:
            //   1. Searches Foundry memory by userId and injects relevant context before RunAsync.
            //   2. Stores the new conversation turn in Foundry memory after RunAsync.
            var response = await memoryContext.Agent.RunAsync(message, session, cancellationToken: cancellationToken);
            var responseText = response.ToString();

            var foundryConversationId = ((ChatClientAgentSession)session).ConversationId
                ?? throw new InvalidOperationException("Foundry conversation ID was not returned by the session.");

            logger.LogInformation(
                "Foundry memory agent response completed. UserId={UserId}, ConversationId={ConversationId}",
                userId, foundryConversationId);

            return new MemoryAgentMessageResult(userId, responseText, foundryConversationId);
        }
        finally
        {
            _currentUserId.Value = null;
        }
    }

    private static IChatClient CreateContextProviderChatClient(IChatClient inner, FoundryMemoryProvider provider)
    {
        var wrapperType = Type.GetType("Microsoft.Agents.AI.AIContextProviderChatClient, Microsoft.Agents.AI", throwOnError: true)
            ?? throw new InvalidOperationException("AIContextProviderChatClient type was not found.");

        var wrapper = Activator.CreateInstance(
            wrapperType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [inner, (IReadOnlyList<AIContextProvider>)[provider]],
            culture: null)
            ?? throw new InvalidOperationException("Failed to create AIContextProviderChatClient wrapper.");

        return (IChatClient)wrapper;
    }

    private static void ValidateRequest(string message, string userId, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            logger.LogWarning("Foundry memory agent request rejected due to empty message. UserId={UserId}", userId);
            throw new ArgumentException("Message is required.", nameof(message));
        }

        if (message.Length > 4000)
        {
            logger.LogWarning("Foundry memory agent request rejected. Message too long: {Length} chars. UserId={UserId}", message.Length, userId);
            throw new ArgumentException("Message must not exceed 4000 characters.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("Foundry memory agent request rejected due to missing userId.");
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (userId.Length > 128 || !UserIdPattern.IsMatch(userId))
        {
            logger.LogWarning("Foundry memory agent request rejected due to invalid userId format.");
            throw new ArgumentException("UserId must be alphanumeric (dots, hyphens, underscores allowed), max 128 characters.", nameof(userId));
        }
    }

    internal static async Task<MemoryStore> GetOrCreateMemoryStoreAsync(
        AIProjectMemoryStores memoryClient, Settings settings, ILogger logger)
    {
        try
        {
            var store = await memoryClient.GetMemoryStoreAsync(settings.MemoryStoreName);
            return store;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            logger.LogInformation("Memory store not found, creating. Name={Name}", settings.MemoryStoreName);

            var definition = new MemoryStoreDefaultDefinition(
                chatModel: settings.AzureAIModelDeploymentName,
                embeddingModel: settings.MemoryEmbeddingModel);
            definition.Options = new MemoryStoreDefaultOptions(
                isUserProfileEnabled: true,
                isChatSummaryEnabled: true);

            var created = await memoryClient.CreateMemoryStoreAsync(
                name: settings.MemoryStoreName,
                definition: definition,
                description: "Memory store for Agent Hub memory agent");
            return created;
        }
    }

    /// <summary>
    /// Searches Foundry memory for the given scope. Used by MemoryAuditService for inspection.
    /// </summary>
    internal static async Task<MemoryStoreSearchResponse> SearchMemoriesAsync(
        AIProjectMemoryStores memoryClient,
        string memoryStoreName,
        string scope,
        string query,
        CancellationToken cancellationToken)
    {
        var request = new MemorySearchProtocolRequest(
            scope,
            [new InputItemMessage("message", "user", query)],
            new MemorySearchProtocolRequestOptions(5));

        var result = await memoryClient.SearchMemoriesAsync(
            memoryStoreName,
            BinaryContent.Create(BinaryData.FromObjectAsJson(request, JsonSerializerOptions.Default)),
            new System.ClientModel.Primitives.RequestOptions { CancellationToken = cancellationToken });

        return (MemoryStoreSearchResponse)result;
    }

    private static async Task<ProjectsAgentRecord> GetOrCreateAgentAsync(
        AIProjectClient client, string agentName, Settings settings, ILogger logger)
    {
        try
        {
            var agent = await client.AgentAdministrationClient.GetAgentAsync(agentName);
            return agent;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            logger.LogInformation(
                "Foundry memory agent not found, creating. AgentName={AgentName}, Model={Model}",
                agentName, settings.AzureAIModelDeploymentName);

            var definition = new DeclarativeAgentDefinition(model: settings.AzureAIModelDeploymentName)
            {
                Instructions = KaiCharterSystemPrompt
            };

            var options = new ProjectsAgentVersionCreationOptions(definition);
            await client.AgentAdministrationClient.CreateAgentVersionAsync(agentName, options);

            logger.LogInformation("Foundry memory agent created. AgentName={AgentName}", agentName);
            return await client.AgentAdministrationClient.GetAgentAsync(agentName);
        }
    }

    private sealed record InputItemMessage(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record MemorySearchProtocolRequest(
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("items")] InputItemMessage[] Items,
        [property: JsonPropertyName("options")] MemorySearchProtocolRequestOptions Options);

    private sealed record MemorySearchProtocolRequestOptions(
        [property: JsonPropertyName("max_memories")] int MaxMemories);
}

public record MemoryAgentMessageResult(string UserId, string Response, string ConversationId);

#pragma warning restore OPENAI001
#pragma warning restore MAAI001
#pragma warning restore AAIP001
