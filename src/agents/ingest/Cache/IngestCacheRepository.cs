using System.Data.SQLite;
using Grimoire.Ingest.Models;

namespace Grimoire.Ingest.Cache;

public class IngestCacheRepository
{
    private readonly string _connectionString;

    public IngestCacheRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        var sql = """
            CREATE TABLE IF NOT EXISTS IngestRecords (
                FilePath TEXT PRIMARY KEY,
                Sha256 TEXT NOT NULL,
                Status TEXT NOT NULL,
                ProcessedAt TEXT NOT NULL,
                ChunkCount INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT,
                UserCorrections TEXT,
                FeedbackAction TEXT,
                FeedbackTag TEXT
            );

            CREATE TABLE IF NOT EXISTS ConversationTurns (
                ConversationId TEXT NOT NULL,
                TurnIndex INTEGER NOT NULL,
                Role TEXT NOT NULL,
                Message TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (ConversationId, TurnIndex)
            );

            CREATE TABLE IF NOT EXISTS FeedbackRequests (
                RequestId TEXT PRIMARY KEY,
                RunId TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                Reason TEXT NOT NULL,
                RaisedAt TEXT NOT NULL,
                ResolvedAt TEXT
            );
            """;

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SQLiteCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IngestRecord?> GetRecordAsync(string filePath)
    {
        const string sql = """
            SELECT FilePath, Sha256, Status, ProcessedAt, ChunkCount,
                   ErrorMessage, UserCorrections, FeedbackAction, FeedbackTag
            FROM IngestRecords
            WHERE FilePath = @FilePath
            """;

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FilePath", filePath);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new IngestRecord
        {
            FilePath = reader.GetString(0),
            Sha256 = reader.GetString(1),
            Status = Enum.Parse<IngestStatus>(reader.GetString(2)),
            ProcessedAt = DateTimeOffset.Parse(reader.GetString(3)),
            ChunkCount = reader.GetInt32(4),
            ErrorMessage = reader.IsDBNull(5) ? null : reader.GetString(5),
            UserCorrections = reader.IsDBNull(6) ? null : reader.GetString(6),
            FeedbackAction = reader.IsDBNull(7) ? null : reader.GetString(7),
            FeedbackTag = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    public async Task SaveRecordAsync(IngestRecord record)
    {
        const string sql = """
            INSERT INTO IngestRecords
                (FilePath, Sha256, Status, ProcessedAt, ChunkCount, ErrorMessage, UserCorrections, FeedbackAction, FeedbackTag)
            VALUES
                (@FilePath, @Sha256, @Status, @ProcessedAt, @ChunkCount, @ErrorMessage, @UserCorrections, @FeedbackAction, @FeedbackTag)
            ON CONFLICT(FilePath) DO UPDATE SET
                Sha256 = excluded.Sha256,
                Status = excluded.Status,
                ProcessedAt = excluded.ProcessedAt,
                ChunkCount = excluded.ChunkCount,
                ErrorMessage = excluded.ErrorMessage,
                UserCorrections = excluded.UserCorrections,
                FeedbackAction = excluded.FeedbackAction,
                FeedbackTag = excluded.FeedbackTag
            """;

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@FilePath", record.FilePath);
        cmd.Parameters.AddWithValue("@Sha256", record.Sha256);
        cmd.Parameters.AddWithValue("@Status", record.Status.ToString());
        cmd.Parameters.AddWithValue("@ProcessedAt", record.ProcessedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ChunkCount", record.ChunkCount);
        cmd.Parameters.AddWithValue("@ErrorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UserCorrections", (object?)record.UserCorrections ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FeedbackAction", (object?)record.FeedbackAction ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FeedbackTag", (object?)record.FeedbackTag ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<FeedbackRequest?> GetFeedbackRequestAsync(string requestId)
    {
        const string sql = """
            SELECT RequestId, RunId, FilePath, Reason, RaisedAt, ResolvedAt
            FROM FeedbackRequests
            WHERE RequestId = @RequestId
            """;

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@RequestId", requestId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new FeedbackRequest
        {
            RequestId = reader.GetString(0),
            RunId = reader.GetString(1),
            FilePath = reader.GetString(2),
            Reason = Enum.Parse<FeedbackReason>(reader.GetString(3)),
            RaisedAt = DateTimeOffset.Parse(reader.GetString(4)),
            ResolvedAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))
        };
    }

    public async Task SaveFeedbackRequestAsync(FeedbackRequest request)
    {
        const string sql = """
            INSERT INTO FeedbackRequests (RequestId, RunId, FilePath, Reason, RaisedAt, ResolvedAt)
            VALUES (@RequestId, @RunId, @FilePath, @Reason, @RaisedAt, @ResolvedAt)
            ON CONFLICT(RequestId) DO UPDATE SET
                ResolvedAt = excluded.ResolvedAt
            """;

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@RequestId", request.RequestId);
        cmd.Parameters.AddWithValue("@RunId", request.RunId);
        cmd.Parameters.AddWithValue("@FilePath", request.FilePath);
        cmd.Parameters.AddWithValue("@Reason", request.Reason.ToString());
        cmd.Parameters.AddWithValue("@RaisedAt", request.RaisedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ResolvedAt", (object?)request.ResolvedAt?.ToString("o") ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkFeedbackResolvedAsync(string requestId, DateTimeOffset resolvedAt)
    {
        const string sql = "UPDATE FeedbackRequests SET ResolvedAt = @ResolvedAt WHERE RequestId = @RequestId";

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@RequestId", requestId);
        cmd.Parameters.AddWithValue("@ResolvedAt", resolvedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveConversationTurnAsync(
        string conversationId,
        int turnIndex,
        string role,
        string message,
        DateTimeOffset createdAt)
    {
        const string sql = """
            INSERT INTO ConversationTurns (ConversationId, TurnIndex, Role, Message, CreatedAt)
            VALUES (@ConversationId, @TurnIndex, @Role, @Message, @CreatedAt)
            ON CONFLICT(ConversationId, TurnIndex) DO UPDATE SET
                Message = excluded.Message
            """;

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = new SQLiteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@ConversationId", conversationId);
        cmd.Parameters.AddWithValue("@TurnIndex", turnIndex);
        cmd.Parameters.AddWithValue("@Role", role);
        cmd.Parameters.AddWithValue("@Message", message);
        cmd.Parameters.AddWithValue("@CreatedAt", createdAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }
}
