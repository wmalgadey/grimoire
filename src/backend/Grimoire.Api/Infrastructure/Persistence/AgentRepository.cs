using System.Data;
using System.Data.SQLite;
using System.Text.Json;
using Grimoire.Api.Core.Domain;

namespace Grimoire.Api.Infrastructure.Persistence;

/// <summary>
/// SQLite-based repository for persisting agent descriptors and jobs.
/// Each operation is wrapped in an implicit transaction.
/// </summary>
public class AgentRepository
{
    private readonly string _connectionString;

    public AgentRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Saves or updates an agent descriptor to the database.
    /// </summary>
    public async Task SaveAgentDescriptorAsync(AgentDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        descriptor.Validate();

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO AgentDescriptors (AgentId, Name, Status, Capabilities, RegisteredAt, LastHealthCheckAt)
            VALUES (@agentId, @name, @status, @capabilities, @registeredAt, @lastHealthCheckAt)
            ON CONFLICT(AgentId) DO UPDATE SET
                Name = @name,
                Status = @status,
                Capabilities = @capabilities,
                LastHealthCheckAt = @lastHealthCheckAt";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@agentId", descriptor.AgentId);
        command.Parameters.AddWithValue("@name", descriptor.Name);
        command.Parameters.AddWithValue("@status", (int)descriptor.Status);
        command.Parameters.AddWithValue("@capabilities", descriptor.Capabilities != null ? JsonSerializer.Serialize(descriptor.Capabilities) : DBNull.Value);
        command.Parameters.AddWithValue("@registeredAt", descriptor.RegisteredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@lastHealthCheckAt", descriptor.LastHealthCheckAt?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves a single agent descriptor by ID, or null if not found.
    /// </summary>
    public async Task<AgentDescriptor?> GetAgentDescriptorAsync(string agentId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "SELECT AgentId, Name, Status, Capabilities, RegisteredAt, LastHealthCheckAt FROM AgentDescriptors WHERE AgentId = @agentId";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@agentId", agentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadAgentDescriptor(reader);
        }

        return null;
    }

    /// <summary>
    /// Retrieves all agent descriptors.
    /// </summary>
    public async Task<List<AgentDescriptor>> GetAllAgentDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = new List<AgentDescriptor>();

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "SELECT AgentId, Name, Status, Capabilities, RegisteredAt, LastHealthCheckAt FROM AgentDescriptors ORDER BY RegisteredAt";

        await using var command = new SQLiteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            descriptors.Add(ReadAgentDescriptor(reader));
        }

        return descriptors;
    }

    /// <summary>
    /// Saves or updates an agent job to the database.
    /// </summary>
    public async Task SaveAgentJobAsync(AgentJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO AgentJobs (JobId, AgentId, Payload, Status, CreatedAt, StartedAt, CompletedAt, FailedAt, ErrorMessage)
            VALUES (@jobId, @agentId, @payload, @status, @createdAt, @startedAt, @completedAt, @failedAt, @errorMessage)
            ON CONFLICT(JobId) DO UPDATE SET
                Status = @status,
                StartedAt = @startedAt,
                CompletedAt = @completedAt,
                FailedAt = @failedAt,
                ErrorMessage = @errorMessage";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@jobId", job.JobId);
        command.Parameters.AddWithValue("@agentId", job.AgentId);
        command.Parameters.AddWithValue("@payload", job.Payload);
        command.Parameters.AddWithValue("@status", (int)job.Status);
        command.Parameters.AddWithValue("@createdAt", job.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("@startedAt", job.StartedAt?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@completedAt", job.CompletedAt?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@failedAt", job.FailedAt?.ToUniversalTime().ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@errorMessage", job.ErrorMessage ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves all jobs with a specific status.
    /// </summary>
    public async Task<List<AgentJob>> GetAgentJobsByStatusAsync(JobStatus status, CancellationToken cancellationToken = default)
    {
        var jobs = new List<AgentJob>();

        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "SELECT JobId, AgentId, Payload, Status, CreatedAt, StartedAt, CompletedAt, FailedAt, ErrorMessage FROM AgentJobs WHERE Status = @status";

        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@status", (int)status);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadAgentJob(reader));
        }

        return jobs;
    }

    /// <summary>
    /// Recovers all agent state from the database on startup.
    /// Returns agents and jobs for reconstruction of in-memory state.
    /// </summary>
    public async Task<(List<AgentDescriptor> Agents, List<AgentJob> Jobs)> RecoverStateAsync(CancellationToken cancellationToken = default)
    {
        var agents = await GetAllAgentDescriptorsAsync(cancellationToken);

        var jobs = new List<AgentJob>();
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = "SELECT JobId, AgentId, Payload, Status, CreatedAt, StartedAt, CompletedAt, FailedAt, ErrorMessage FROM AgentJobs";

        await using var command = new SQLiteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadAgentJob(reader));
        }

        return (agents, jobs);
    }

    private static AgentDescriptor ReadAgentDescriptor(IDataReader reader)
    {
        var capabilitiesJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        var capabilities = capabilitiesJson != null ? JsonSerializer.Deserialize<string[]>(capabilitiesJson) : null;

        return new AgentDescriptor
        {
            AgentId = reader.GetString(0),
            Name = reader.GetString(1),
            Status = (AgentStatus)reader.GetInt32(2),
            Capabilities = capabilities,
            RegisteredAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastHealthCheckAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    private static AgentJob ReadAgentJob(IDataReader reader)
    {
        var jobId = reader.GetString(0);
        var agentId = reader.GetString(1);
        var payload = reader.GetString(2);
        var status = (JobStatus)reader.GetInt32(3);
        var createdAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var startedAt = reader.IsDBNull(5) ? (DateTime?)null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var completedAt = reader.IsDBNull(6) ? (DateTime?)null : DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var failedAt = reader.IsDBNull(7) ? (DateTime?)null : DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var errorMessage = reader.IsDBNull(8) ? null : reader.GetString(8);

        return new AgentJob(jobId, agentId, payload, status, createdAt, startedAt, completedAt, failedAt, errorMessage);
    }
}
