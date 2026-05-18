using Azure.AI.OpenAI;
using Azure.Identity;
using AgentHub.API.services.conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AgentHub.API.services.session;

namespace AgentHub.API.Agents;

public static class DemoAzureOpenAIAgent
{
    public static AIAgent Create(Settings settings)
    {
        if (settings.AzureOpenAIEndpoint is null)
        {
            throw new InvalidOperationException(
                "Demo AOAI agent requires a dedicated Azure OpenAI endpoint. Set AgentHub:AzureOpenAIEndpoint or AZURE_OPENAI_ENDPOINT, and ensure your signed-in identity has the 'Cognitive Services OpenAI User' role on that Azure OpenAI resource.");
        }

        return new AzureOpenAIClient(settings.AzureOpenAIEndpoint, new DefaultAzureCredential())
            .GetChatClient(settings.AzureAIModelDeploymentName)
            .AsIChatClient()
            .AsAIAgent(
                instructions: "You are a friendly assistant. Keep your answers brief.",
                name: "demo-basic-aoai-agent");
    }

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
            return agent.RunAsync(
                message,
                agentSession,
                cancellationToken: cancellationToken);
        }

        logger.LogInformation(
            "Rehydrating session from persisted conversation history. ConversationId={ConversationId}, HistoryCount={HistoryCount}",
            session.ConversationId,
            session.History.Count);

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
}

public record AgentMessageResult(Guid ConversationId, string Response);
