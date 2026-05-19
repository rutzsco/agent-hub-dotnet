using System.ClientModel;
using System.Text.RegularExpressions;
using AgentHub.API.Agents;
using AgentHub.API.services.conversations;
using AgentHub.API.services.session;
using AgentHub.API.Services;
using AgentHub.API.Services.Memory;
using AgentHub.API.Services.Skills.Validation;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace AgentHub.API.Routes;

/// <summary>
/// Minimal-API endpoint mappings for every agent and memory-management route in AgentHub.
/// Kept as an extension method on <see cref="WebApplication"/> so <c>Program.cs</c> stays a one-liner.
/// </summary>
public static partial class AgentRoutes
{
    /// <summary>Source-generated regex validating userId path/body inputs.</summary>
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._%+@\-]{0,127}$", RegexOptions.Compiled)]
    internal static partial Regex UserIdPattern();

    /// <summary>
    /// Registers all AgentHub endpoints: demo agent, Foundry demo agent, Foundry memory agent,
    /// memory inspect/delete, and conversation history.
    /// </summary>
    public static WebApplication MapAgentRoutes(this WebApplication app)
    {
        app.MapPost("/agents/demo-aoai-agent", async (
            [FromKeyedServices("demo")] AIAgent agent,
            IConversationSessionManager sessionManager,
            AgentRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.DemoAgentRoute");
            logger.LogInformation(
                "Received demo agent request. ConversationId={ConversationId}, MessageLength={MessageLength}",
                request.ConversationId,
                request.Message?.Length ?? 0);

            try
            {
                var result = await DemoAzureOpenAIAgent.ProcessMessage(
                    agent,
                    sessionManager,
                    request.Message,
                    request.ConversationId,
                    logger,
                    cancellationToken);

                logger.LogInformation(
                    "Demo agent response completed. ConversationId={ConversationId}, ResponseLength={ResponseLength}",
                    result.ConversationId,
                    result.Response.Length);

                return Results.Ok(new AgentRunResult(result.ConversationId, result.Response));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

#pragma warning disable OPENAI001 // FoundryAgent is experimental
        app.MapPost("/agents/demo-foundry-basic-agent", async (
            IServiceProvider serviceProvider,
            IConversationSessionManager sessionManager,
            AgentRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.FoundryAgentRoute");
            logger.LogInformation(
                "Received Foundry agent request. ConversationId={ConversationId}, MessageLength={MessageLength}",
                request.ConversationId,
                request.Message?.Length ?? 0);

            try
            {
                var agent = serviceProvider.GetRequiredKeyedService<FoundryAgent>("foundry-demo");
                var result = await FoundryDemoAgent.ProcessMessage(
                    agent,
                    sessionManager,
                    request.Message,
                    request.ConversationId,
                    logger,
                    cancellationToken);

                logger.LogInformation(
                    "Foundry agent response completed. ConversationId={ConversationId}, ResponseLength={ResponseLength}",
                    result.ConversationId,
                    result.Response.Length);

                return Results.Ok(new AgentRunResult(result.ConversationId, result.Response));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex) when (IsFoundryNotConfigured(ex))
            {
                return FoundryNotConfigured(ex);
            }
        });
#pragma warning restore OPENAI001

        app.MapPost("/agents/foundryMemoryAgent", async (
            //FoundryMemoryContext memoryContext,
            PromptValidationSkill validationSkill,
            IServiceProvider serviceProvider,
            MemoryAgentRequest request,
            HttpContext httpContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.FoundryMemoryAgentRoute");

            httpContext.Request.Headers["x-memory-user-id"] = request.UserId;
            logger.LogInformation(
                "Received Foundry memory agent request. UserId={UserId}, MessageLength={MessageLength}",
                request.UserId,
                request.Message?.Length ?? 0);
            logger.LogDebug("Message={Message}, ConversationId={ConversationId} (null = new conversation)",
                request.Message, request.ConversationId);

            try
            {
                var memoryContext = serviceProvider.GetRequiredService<FoundryMemoryContext>();
                var result = await FoundryMemoryAgent.ProcessMessage(
                    memoryContext,
                    validationSkill,
                    request.Message,
                    request.UserId,
                    request.ConversationId,
                    logger,
                    cancellationToken);

                logger.LogInformation(
                    "Foundry memory agent response completed. UserId={UserId}, ResponseLength={ResponseLength}",
                    result.UserId,
                    result.Response.Length);

                return Results.Ok(new MemoryAgentRunResult(result.UserId, result.Response, result.ConversationId));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (ClientResultException ex) when (IsFoundryContentFilterError(ex))
            {
                logger.LogWarning(
                    ex,
                    "Foundry memory request blocked by content filter. UserId={UserId}, ConversationId={ConversationId}",
                    request.UserId,
                    request.ConversationId);

                return Results.BadRequest(new
                {
                    error = "The request was blocked by content filtering. Please rephrase your message and retry.",
                    code = "content_filter"
                });
            }
            catch (InvalidOperationException ex) when (IsFoundryNotConfigured(ex))
            {
                return FoundryNotConfigured(ex);
            }
        });

        app.MapGet("/users/{userId}/memory", async (
            string userId,
            IServiceProvider serviceProvider,
            string? topic,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.MemoryAuditRoute");

            if (!UserIdPattern().IsMatch(userId))
            {
                logger.LogWarning("Invalid userId format in memory inspect request. UserId={UserId}", userId);
                return Results.BadRequest("Invalid userId format.");
            }

            try
            {
                var auditService = serviceProvider.GetRequiredService<MemoryAuditService>();
                var result = await auditService.InspectAsync(userId, topic, cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (IsFoundryNotConfigured(ex))
            {
                return FoundryNotConfigured(ex);
            }
        });

        app.MapDelete("/users/{userId}/memory", async (
            string userId,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.MemoryDeleteRoute");

            if (!UserIdPattern().IsMatch(userId))
            {
                logger.LogWarning("Invalid userId format in memory delete request. UserId={UserId}", userId);
                return Results.BadRequest("Invalid userId format.");
            }

            IMemoryAuditRepository? auditRepository = null;
            try
            {
                var auditService = serviceProvider.GetRequiredService<MemoryAuditService>();
                auditRepository = serviceProvider.GetRequiredService<IMemoryAuditRepository>();
                var result = await auditService.DeleteAsync(userId, cancellationToken);
                
                // Check if deletion was actually successful
                if (!result.FoundryScopeDeleted)
                {
                    var errorMsg = $"Failed to delete memory scope for user {userId}. User may not exist or scope not found.";
                    logger.LogWarning("Memory deletion failed. UserId={UserId}, FoundryDeleted={FoundryDeleted}", 
                        userId, result.FoundryScopeDeleted);
                    
                    var auditMessage = $"Attempted memory deletion for non-existent user or empty scope";
                    await auditRepository.LogMemoryDeletionAsync(
                        userId,
                        "foundry-memory",
                        auditMessage,
                        wasSuccessful: false,
                        errorMessage: errorMsg,
                        cancellationToken);

                    return Results.BadRequest(new { error = errorMsg, result });
                }

                var successMessage = $"Memory scope successfully deleted";
                await auditRepository.LogMemoryDeletionAsync(
                    userId,
                    "foundry-memory",
                    successMessage,
                    wasSuccessful: true,
                    errorMessage: null,
                    cancellationToken);

                logger.LogInformation(
                    "Memory deletion completed and audited. UserId={UserId}, FoundryDeleted={FoundryDeleted}",
                    userId, result.FoundryScopeDeleted);

                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (IsFoundryNotConfigured(ex))
            {
                return FoundryNotConfigured(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Memory deletion failed with exception. UserId={UserId}", userId);

                if (auditRepository is not null)
                {
                    var errorAuditMessage = $"Memory deletion error: {ex.GetType().Name}";
                    await auditRepository.LogMemoryDeletionAsync(
                        userId,
                        "foundry-memory",
                        errorAuditMessage,
                        wasSuccessful: false,
                        errorMessage: ex.Message,
                        cancellationToken);
                }

                throw;
            }
        });

        app.MapGet("/conversations/{conversationId:guid}/history", async (
            Guid conversationId,
            IConversationSessionManager sessionManager,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AgentHub.ConversationHistoryRoute");
            logger.LogInformation("Fetching conversation history. ConversationId={ConversationId}", conversationId);

            var history = await sessionManager.GetHistoryAsync(conversationId, cancellationToken);

            logger.LogInformation(
                "Conversation history returned. ConversationId={ConversationId}, MessageCount={MessageCount}",
                conversationId,
                history.Count);

            return Results.Ok(new ConversationHistoryResult(conversationId, history));
        });

        return app;
    }

    /// <summary>
    /// Identifies Foundry responses that were rejected by the content-safety filter so the route
    /// can return a friendly 400 rather than a generic 500.
    /// </summary>
    private static bool IsFoundryContentFilterError(ClientResultException ex)
    {
        if (ex.Status != 400)
        {
            return false;
        }

        var message = ex.Message;
        return message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
               || message.Contains("invalid_request_error", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFoundryNotConfigured(InvalidOperationException ex)
    {
        return ex.Message.StartsWith("Foundry agents are not configured.", StringComparison.Ordinal);
    }

    private static IResult FoundryNotConfigured(InvalidOperationException ex)
    {
        return Results.Problem(
            title: "Foundry support is not configured",
            detail: ex.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

}

public record AgentRequest(string Message, Guid? ConversationId);

public record AgentRunResult(Guid ConversationId, string Response);

public record MemoryAgentRequest(string Message, string UserId, string? ConversationId = null);

public record MemoryAgentRunResult(string UserId, string Response, string ConversationId);

public record ConversationHistoryResult(Guid ConversationId, IReadOnlyList<ConversationMessage> Messages);
