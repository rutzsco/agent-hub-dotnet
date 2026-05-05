namespace AgentHub.Persistence;

public sealed record ConversationMessage(
    long Id,
    Guid ConversationId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents a tamper-proof audit log entry for memory deletion operations.
/// Stored in PostgreSQL for compliance and accountability.
/// </summary>
public sealed record MemoryDeletionAuditEntry(
    long Id,
    string UserId,
    string MemoryStoreName,
    string AuditMessage,
    bool WasSuccessful,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

/// <summary>
/// Contract for memory deletion audit logging operations.
/// </summary>
public interface IMemoryAuditRepository
{
    /// <summary>
    /// Logs a memory deletion event to the audit trail.
    /// </summary>
    Task LogMemoryDeletionAsync(
        string userId,
        string memoryStoreName,
        string auditMessage,
        bool wasSuccessful,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all deletion audit entries for a specific user.
    /// </summary>
    Task<IReadOnlyList<MemoryDeletionAuditEntry>> GetUserDeletionHistoryAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
