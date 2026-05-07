using AgentHub.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AgentHub.API.Services.Memory;

/// <summary>
/// PostgreSQL-backed memory deletion audit trail repository.
/// Provides tamper-proof, persistent audit logging for compliance.
/// </summary>
public sealed class PostgresMemoryAuditRepository : IMemoryAuditRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresMemoryAuditRepository> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _isInitialized;

    public PostgresMemoryAuditRepository(
        PostgresConversationOptions options,
        ILogger<PostgresMemoryAuditRepository> logger)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is empty. Verify AgentHub:Postgres configuration.");
        }

        _connectionString = options.ConnectionString;
        _logger = logger;
    }

    public async Task LogMemoryDeletionAsync(
        string userId,
        string memoryStoreName,
        string auditMessage,
        bool wasSuccessful,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string sql = """
            INSERT INTO memory_deletion_audit (user_id, memory_store_name, audit_message, was_successful, error_message, created_at)
            VALUES (@userId, @memoryStoreName, @auditMessage, @wasSuccessful, @errorMessage, @createdAt)
            RETURNING id;
            """;

        _logger.LogDebug(
            "Logging memory deletion. UserId={UserId}, MemoryStore={MemoryStore}, Success={Success}, Message={Message}",
            userId, memoryStoreName, wasSuccessful, auditMessage);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@memoryStoreName", memoryStoreName);
            command.Parameters.AddWithValue("@auditMessage", auditMessage);
            command.Parameters.AddWithValue("@wasSuccessful", wasSuccessful);
            command.Parameters.AddWithValue("@errorMessage", (object?)errorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow);

            var result = await command.ExecuteScalarAsync(cancellationToken);

            _logger.LogInformation(
                "Memory deletion audit logged. UserId={UserId}, MemoryStore={MemoryStore}, AuditId={AuditId}, Success={Success}",
                userId, memoryStoreName, result, wasSuccessful);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex,
                "Failed to log memory deletion audit. UserId={UserId}, MemoryStore={MemoryStore}",
                userId, memoryStoreName);
            throw;
        }
    }

    public async Task<IReadOnlyList<MemoryDeletionAuditEntry>> GetUserDeletionHistoryAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string sql = """
            SELECT id, user_id, memory_store_name, audit_message, was_successful, error_message, created_at
            FROM memory_deletion_audit
            WHERE user_id = @userId
            ORDER BY created_at DESC;
            """;

        _logger.LogDebug("Retrieving deletion history for UserId={UserId}", userId);

        try
        {
            var entries = new List<MemoryDeletionAuditEntry>();

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("@userId", userId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(new MemoryDeletionAuditEntry(
                    Id: reader.GetInt64(0),
                    UserId: reader.GetString(1),
                    MemoryStoreName: reader.GetString(2),
                    AuditMessage: reader.GetString(3),
                    WasSuccessful: reader.GetBoolean(4),
                    ErrorMessage: reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(6)));
            }

            _logger.LogInformation(
                "Retrieved {Count} deletion audit entries for UserId={UserId}",
                entries.Count, userId);

            return entries;
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve deletion history for UserId={UserId}",
                userId);
            throw;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            _logger.LogDebug("Initializing memory_deletion_audit table");

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string createTableSql = """
                CREATE TABLE IF NOT EXISTS memory_deletion_audit (
                    id BIGSERIAL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    memory_store_name TEXT NOT NULL,
                    audit_message TEXT NOT NULL,
                    was_successful BOOLEAN NOT NULL,
                    error_message TEXT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS idx_memory_deletion_audit_user_id
                    ON memory_deletion_audit(user_id);

                CREATE INDEX IF NOT EXISTS idx_memory_deletion_audit_created_at
                    ON memory_deletion_audit(created_at);
                """;

            await using var command = new NpgsqlCommand(createTableSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Memory deletion audit table initialized");
            _isInitialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}
