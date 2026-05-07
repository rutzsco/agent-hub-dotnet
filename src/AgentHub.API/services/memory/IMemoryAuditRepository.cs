namespace AgentHub.API.Services.Memory;

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