namespace AgentHub.SessionState;

using AgentHub.API.services.conversations;

public sealed record ConversationSessionContext(
	Guid ConversationId,
	object Session,
	IReadOnlyList<ConversationMessage> History,
	bool RequiresHistoryReplay);
