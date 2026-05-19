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
/// Foundry-managed conversational agent with per-user long-term memory.
///
/// Memory model:
/// - Short-term (per conversation): stored server-side as a Foundry thread
///   (ChatClientAgentSession). We only carry the conversationId on the wire;
///   prior turns are never replayed by the client.
/// - Long-term (per user): stored server-side in a Foundry Memory Store
///   (AIProjectMemoryStores) created/resolved at startup. Foundry owns
///   embeddings, vector search, user profile, and chat summaries.
///
/// Session hydration:
/// - New conversation (conversationId == null) -> Agent.CreateConversationSessionAsync
///   creates a fresh Foundry thread; Foundry returns a new conversationId.
/// - Follow-up (conversationId provided) -> BaseAgent.CreateSessionAsync(conversationId)
///   resumes the existing thread. Foundry rehydrates context from its own state.
///
/// Long-term memory orchestration:
/// - FoundryMemoryProvider is attached client-side via the AsAIAgent chatClient
///   transform (AIContextProviderChatClient). The provider:
///     1. Before each run: searches the memory store scoped to the current
///        userId and injects relevant memories into the prompt.
///     2. After each run: persists the new user/assistant turn back to the
///        memory store under the same scope.
/// - Per-user scoping is provided by setting an AsyncLocal&lt;string?&gt; _currentUserId
///   for the duration of the request; the provider reads it via stateInitializer.
/// - Our process never holds memory bytes; it only supplies the scope key and
///   triggers retrieve/persist hooks.
///
/// System prompt:
/// - Sourced from AgentHub:MemoryAgentInstructions in configuration; falls back
///   to KaiCharterSystemPrompt constant.
/// - Foundry bakes instructions at agent creation. To roll out a new prompt,
///   bump AgentHub:FoundryAgentName so a fresh agent is provisioned.
///
/// Naming note:
/// - IChatClient / "chatClient transform" refers to the Microsoft.Extensions.AI
///   abstraction used for client-side middleware. It is NOT the Azure OpenAI
///   /chat/completions REST endpoint. Foundry decides Chat Completions vs.
///   Responses API server-side based on the model and tools attached.
/// </summary>
/// <summary>
/// Bundle of Foundry handles required to drive the memory agent end-to-end:
/// the <see cref="AIAgent"/> wrapper, the underlying <see cref="ChatClientAgent"/> (for session resumption),
/// the memory store client, and the active store name.
/// </summary>
public sealed class FoundryMemoryContext
{
    /// <summary>The Foundry agent wrapper, wired with the <c>FoundryMemoryProvider</c> chat-client transform.</summary>
    public required FoundryAgent Agent { get; init; }
    /// <summary>The underlying chat-client agent, used to resume existing Foundry threads by conversation id.</summary>
    public required ChatClientAgent BaseAgent { get; init; }
    /// <summary>Client for direct Foundry memory store operations (search, delete).</summary>
    public required AIProjectMemoryStores MemoryClient { get; init; }
    /// <summary>Name of the active Foundry memory store.</summary>
    public required string MemoryStoreName { get; init; }
}

/// <summary>
/// Factory and message-processing helpers for the Foundry-managed memory agent.
/// See the file-header comment on <see cref="FoundryMemoryContext"/> for the full memory model.
/// </summary>
public static class FoundryMemoryAgent
{
    /// <summary>Default Foundry agent name when no override is configured.</summary>
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
          "intent": "field_help" | "review" | "section_review" | "chat" | "freeform",
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

        # Field semantics (DO NOT confuse these)
        Each field is independent and serves a different purpose. Never suggest
        that one field's value should be copied into, replaced by, or kept in
        sync with another field's value. They live side-by-side and together
        tell the full story.

        - Problem Statement (Summary & Schedule): a *narrative* sentence (or
          two) describing the current state in business language. It MAY
          mention numbers from the KPI fields for context, but it is prose.
          It is NOT a place to put a single number, a unit, or a KPI value.
        - KPI Target: the numeric goal value only. Example: "100 units/week"
          or "95% on-time".
        - KPI Actual: the numeric current measured value only. Example:
          "90 units/week" or "82% on-time". This must be a DIFFERENT number
          than KPI Target (otherwise there is no gap).
        - KPI Gap: the numeric difference (Target - Actual) with the same unit.
          Example: "10 units/week" or "13 percentage points".
        - KPI Trend: a short directional phrase. Example: "flat for 3 months",
          "declining since Q2", "improving slowly".
        - Process Name: the named business process. Example: "NA Sales
          Fulfillment".
        - Process Mapped: yes/no plus link or reference. Example: "Yes -
          process map v3 in SharePoint".

        Hard rules for these fields:
        - Never recommend that Problem Statement should equal or restate KPI
          Actual / Target / Gap. The Problem Statement may *cite* those
          numbers but must add narrative context (where, when, who is
          impacted, time horizon).
        - Never recommend changing the unit in Problem Statement to "match"
          KPI fields. Units belong on the KPI fields. The narrative inherits
          them by reference.
        - If KPI Target == KPI Actual, do NOT suggest making them equal.
          Instead, flag this as a problem (no gap = no improvement
          opportunity) and ask the user to verify.
        - When suggesting wording for the Problem Statement, write a sentence
          that USES the KPI numbers from sectionValues, not one that DUPLICATES
          a single KPI field's value.

        # Response format
        Respond in Markdown, kept short (under ~200 words). Use this structure
        when the intent is field_help:

        **Tips for "<field label>"**
        - 1 to 3 short, specific bullets.

        **Suggested wording**
        > A single concrete example the user could paste into the field, tailored
        > to their currentValue and sectionValues.

        When the intent is section_review:

        **Section: <section title>**
        - Bulleted critique covering what is missing, what could be sharper, and
          what to write next.
        - End with one sentence naming the single most important next step.

        When the intent is review (per-field framework check):

        Run a strict rubric-style review of `currentValue` against the framework
        for this field (see "Frameworks to apply"). Use this exact format:

        **Review: "<field label>"**
        - ✅ <rule that passes> — short evidence quote from currentValue.
        - ❌ <rule that fails> — what is missing or wrong, in <= 12 words.
        - ⚠️ <rule that is borderline> — what would tighten it, in <= 12 words.

        **Suggested wording**
        > A revised version of currentValue that satisfies every ❌ and ⚠️ rule.
        > Reuse the user's own numbers and terminology. Never invent numbers.

        If currentValue is empty, list every framework rule as ❌ and write the
        Suggested wording as a fresh draft using only values from sectionValues
        (or example placeholders prefixed with "Example:").

        When the intent is freeform, answer the userMessage directly but stay on
        the topic of the current section/field.

        When the intent is chat:

        Reply conversationally to userMessage in 1-3 short paragraphs of plain
        prose. Stay on topic of Kaizen / process improvement / the current
        charter section. You may reference sectionValues for context, but:
        - Do NOT output the rubric formats above.
        - Do NOT output a "Suggested wording" blockquote (chat replies are not
          meant to be pasted into form fields).
        - Do NOT output bulleted tip lists unless the user explicitly asks.
        - Use persistent memory to remember things the user tells you across
          turns (their team, KPIs, preferred terminology).
        - If the user asks for help filling out a specific field, redirect them
          to use the "Ask AI" or "Review" buttons next to that field instead of
          answering with form-ready content here.

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

    /// <summary>
    /// Initializes the Foundry memory agent: resolves/creates the memory store, attaches the memory provider
    /// as a chat-client transform, and returns a <see cref="FoundryMemoryContext"/> for request-time use.
    /// </summary>
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

    /// <summary>
    /// Validates the request, runs the agent with per-user memory scoping, and returns the assistant reply.
    /// Creates a new Foundry thread when <paramref name="conversationId"/> is null; resumes the existing thread otherwise.
    /// </summary>
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
                Instructions = string.IsNullOrWhiteSpace(settings.MemoryAgentInstructions)
                    ? KaiCharterSystemPrompt
                    : settings.MemoryAgentInstructions
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
