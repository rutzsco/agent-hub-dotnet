using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using AgentHub.API.services.conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AgentHub.API.services.session;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace AgentHub.API.Agents;

/// <summary>
/// Factory and message-processing helpers for the "demo" agent backed directly by Azure OpenAI
/// (no Foundry agent/thread orchestration). State is managed client-side via
/// <see cref="IConversationSessionManager"/> and replayed history.
/// </summary>
public static class DemoAzureOpenAIAgent
{
    private const string ApimSubscriptionKeyHeaderName = "Ocp-Apim-Subscription-Key";

    /// <summary>
    /// Builds an <see cref="AIAgent"/> backed by Azure OpenAI chat completions.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="Settings.AzureOpenAIEndpoint"/> is not configured.</exception>
    public static AIAgent Create(Settings settings)
    {
        var endpoint = settings.RequireAzureOpenAIEndpoint();
        var client = CreateAzureOpenAIClient(settings, endpoint);

        return client
            .GetChatClient(settings.AzureAIModelDeploymentName)
            .AsIChatClient()
            .AsAIAgent(
                instructions: "You are a friendly assistant. Keep your answers brief.",
                name: "demo-basic-aoai-agent");
    }

    private static AzureOpenAIClient CreateAzureOpenAIClient(Settings settings, Uri endpoint)
    {
        var apimSubscriptionKey = settings.ApimSubscriptionKey;
        var keyToUse = string.IsNullOrWhiteSpace(settings.AzureAIApiKey)
            ? apimSubscriptionKey
            : settings.AzureAIApiKey;

        if (string.IsNullOrWhiteSpace(keyToUse))
        {
            return new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
        }

        if (string.IsNullOrWhiteSpace(apimSubscriptionKey))
        {
            return new AzureOpenAIClient(endpoint, new AzureKeyCredential(keyToUse));
        }

        var options = new AzureOpenAIClientOptions();
        options.AddPolicy(
            new ApimSubscriptionKeyPolicy(apimSubscriptionKey),
            PipelinePosition.PerCall);

        return new AzureOpenAIClient(endpoint, new AzureKeyCredential(keyToUse), options);
    }

    /// <summary>
    /// Validates input, runs the agent within a managed session, persists the turn, and returns the assistant reply.
    /// </summary>
    public static async Task<AgentMessageResult> ProcessMessage(
        AIAgent agent,
        IConversationSessionManager sessionManager,
        string message,
        Guid? conversationId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            logger.LogWarning("Demo agent request rejected due to empty message. ConversationId={ConversationId}", conversationId);
            throw new ArgumentException("Message is required.", nameof(message));
        }

        var session = await sessionManager.GetOrCreateSessionAsync(
            conversationId,
            async _ => await agent.CreateSessionAsync(),
            cancellationToken);

        var response = await RunWithConversationMemoryAsync(
            agent,
            session,
            message,
            logger,
            cancellationToken);

        var responseText = response.ToString();
        await sessionManager.AppendTurnAsync(
            session.ConversationId,
            message,
            responseText,
            cancellationToken);

        return new AgentMessageResult(session.ConversationId, responseText);
    }

    /// <summary>
    /// Runs the agent, replaying persisted history into the session on the first turn after rehydration.
    /// </summary>
    internal static Task<AgentResponse> RunWithConversationMemoryAsync(
        AIAgent agent,
        ConversationSessionContext session,
        string message,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var agentSession = (AgentSession)session.Session;

        if (!session.RequiresHistoryReplay)
        {
            // Fast path: in-process session already holds the running transcript.
            return agent.RunAsync(
                message,
                agentSession,
                cancellationToken: cancellationToken);
        }

        logger.LogInformation(
            "Rehydrating session from persisted conversation history. ConversationId={ConversationId}, HistoryCount={HistoryCount}",
            session.ConversationId,
            session.History.Count);

        // Cold path (e.g., after process restart): replay history so the model sees full context.
        var messages = session.History
            .Select(ToChatMessage)
            .Append(new ChatMessage(ChatRole.User, message));

        return agent.RunAsync(
            messages,
            agentSession,
            cancellationToken: cancellationToken);
    }

    private static ChatMessage ToChatMessage(ConversationMessage message)
    {
        var role = message.Role switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

        return new ChatMessage(role, message.Content);
    }

    private sealed class ApimSubscriptionKeyPolicy(string subscriptionKey) : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set(ApimSubscriptionKeyHeaderName, subscriptionKey);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(
            PipelineMessage message,
            IReadOnlyList<PipelinePolicy> pipeline,
            int currentIndex)
        {
            message.Request.Headers.Set(ApimSubscriptionKeyHeaderName, subscriptionKey);
            await ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}

public record AgentMessageResult(Guid ConversationId, string Response);
