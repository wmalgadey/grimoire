using System.Data;
using System.Data.SQLite;
using Grimoire.Api.Ingest.Models;

namespace Grimoire.Api.Ingest.Persistence;

public class IngestRepository
{
    private readonly string _connectionString;

    public IngestRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task SaveIngestRunAsync(IngestRunRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO IngestRuns (RunId, Status, StartedAt, CompletedAt, TotalFiles, ProcessedFiles, FailedFiles, SkippedFiles, DurationMs, ErrorMessage, FileResults)
            VALUES (@runId, @status, @startedAt, @completedAt, @totalFiles, @processedFiles, @failedFiles, @skippedFiles, @durationMs, @errorMessage, @fileResults)
            ON CONFLICT(RunId) DO UPDATE SET
                Status = @status,
                CompletedAt = @completedAt,
                TotalFiles = @totalFiles,
                ProcessedFiles = @processedFiles,
                FailedFiles = @failedFiles,
                SkippedFiles = @skippedFiles,
                DurationMs = @durationMs,
                ErrorMessage = @errorMessage,
                FileResults = @fileResults";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@runId", record.RunId);
        command.Parameters.AddWithValue("@status", record.Status);
        command.Parameters.AddWithValue("@startedAt", record.StartedAt);
        command.Parameters.AddWithValue("@completedAt", record.CompletedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@totalFiles", record.TotalFiles);
        command.Parameters.AddWithValue("@processedFiles", record.ProcessedFiles);
        command.Parameters.AddWithValue("@failedFiles", record.FailedFiles);
        command.Parameters.AddWithValue("@skippedFiles", record.SkippedFiles);
        command.Parameters.AddWithValue("@durationMs", record.DurationMs);
        command.Parameters.AddWithValue("@errorMessage", record.ErrorMessage ?? (object)DBNull.Value);
        var resultsJson = record.FileResults.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(record.FileResults) : null;
        command.Parameters.AddWithValue("@fileResults", resultsJson ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IngestRunRecord?> GetIngestRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT RunId, Status, StartedAt, CompletedAt, TotalFiles, ProcessedFiles, FailedFiles, SkippedFiles, DurationMs, ErrorMessage, FileResults
            FROM IngestRuns WHERE RunId = @runId";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@runId", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var record = new IngestRunRecord
            {
                RunId = reader.GetString(0),
                Status = reader.GetString(1),
                StartedAt = reader.GetString(2),
                CompletedAt = reader.IsDBNull(3) ? null : reader.GetString(3),
                TotalFiles = reader.GetInt32(4),
                ProcessedFiles = reader.GetInt32(5),
                FailedFiles = reader.GetInt32(6),
                SkippedFiles = reader.GetInt32(7),
                DurationMs = reader.GetInt64(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9)
            };

            if (!reader.IsDBNull(10))
            {
                var resultsJson = reader.GetString(10);
                record.FileResults = System.Text.Json.JsonSerializer.Deserialize<List<FileProcessingResult>>(resultsJson) ?? new();
            }

            return record;
        }

        return null;
    }

    public async Task<bool> IsRunActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "SELECT COUNT(*) FROM IngestRuns WHERE Status = 'Running'";
        await using var command = new SQLiteCommand(sql, connection);
        var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);

        return count > 0;
    }

    public async Task<string?> GetActiveRunIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "SELECT RunId FROM IngestRuns WHERE Status = 'Running' LIMIT 1";
        await using var command = new SQLiteCommand(sql, connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result?.ToString();
    }

    public async Task SaveConversationTurnAsync(ConversationTurnRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO ConversationTurns (ConversationId, TurnIndex, FilePath, Role, Message, CreatedAt)
            VALUES (@conversationId, @turnIndex, @filePath, @role, @message, @createdAt)
            ON CONFLICT(ConversationId, TurnIndex) DO UPDATE SET
                Message = @message,
                Role = @role";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@conversationId", record.ConversationId);
        command.Parameters.AddWithValue("@turnIndex", record.TurnIndex);
        command.Parameters.AddWithValue("@filePath", record.FilePath);
        command.Parameters.AddWithValue("@role", record.Role);
        command.Parameters.AddWithValue("@message", record.Message);
        command.Parameters.AddWithValue("@createdAt", record.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<ConversationTurnRecord>> GetConversationTurnsAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var turns = new List<ConversationTurnRecord>();
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT ConversationId, TurnIndex, FilePath, Role, Message, CreatedAt
            FROM ConversationTurns
            WHERE ConversationId = @conversationId
            ORDER BY TurnIndex ASC";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@conversationId", conversationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            turns.Add(new ConversationTurnRecord(
                ConversationId: reader.GetString(0),
                TurnIndex: reader.GetInt32(1),
                FilePath: reader.GetString(2),
                Role: reader.GetString(3),
                Message: reader.GetString(4),
                CreatedAt: reader.GetString(5)
            ));
        }

        return turns;
    }
}
