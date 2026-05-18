namespace AgentHub.API.Services.Memory;

/// <summary>
/// Represents a tamper-proof audit log entry for memory deletion operations.
/// Stored in PostgreSQL for compliance and accountability.
/// </summary>
public sealed record MemoryDeletionAuditEntry(
    string Id,
    string UserId,
    string MemoryStoreName,
    string AuditMessage,
    bool WasSuccessful,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);
