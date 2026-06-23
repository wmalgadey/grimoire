using System.Data;
using System.Data.SQLite;
using System.Text.Json;
using Grimoire.Api.Agents.Models;

namespace Grimoire.Api.Agents.Persistence;

public class AgentRepository
{
    private readonly string _connectionString;

    public AgentRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

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

    public async Task<AgentDescriptor?> GetAgentDescriptorAsync(string agentId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT AgentId, Name, Status, Capabilities, RegisteredAt, LastHealthCheckAt FROM AgentDescriptors WHERE AgentId = @agentId";
        await using var command = new SQLiteCommand(sql, connection);
        command.Parameters.AddWithValue("@agentId", agentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAgentDescriptor(reader) : null;
    }

    public async Task<List<AgentDescriptor>> GetAllAgentDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = new List<AgentDescriptor>();
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT AgentId, Name, Status, Capabilities, RegisteredAt, LastHealthCheckAt FROM AgentDescriptors ORDER BY RegisteredAt";
        await using var command = new SQLiteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            descriptors.Add(ReadAgentDescriptor(reader));
        return descriptors;
    }

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
            jobs.Add(ReadAgentJob(reader));
        return jobs;
    }

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
            jobs.Add(ReadAgentJob(reader));
        return (agents, jobs);
    }

    private static AgentDescriptor ReadAgentDescriptor(IDataReader reader)
    {
        var capabilitiesJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        return new AgentDescriptor
        {
            AgentId = reader.GetString(0),
            Name = reader.GetString(1),
            Status = (AgentStatus)reader.GetInt32(2),
            Capabilities = capabilitiesJson != null ? JsonSerializer.Deserialize<string[]>(capabilitiesJson) : null,
            RegisteredAt = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastHealthCheckAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    private static AgentJob ReadAgentJob(IDataReader reader)
    {
        return new AgentJob(
            jobId: reader.GetString(0),
            agentId: reader.GetString(1),
            payload: reader.GetString(2),
            status: (JobStatus)reader.GetInt32(3),
            createdAt: DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            startedAt: reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
            completedAt: reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
            failedAt: reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
            errorMessage: reader.IsDBNull(8) ? null : reader.GetString(8)
        );
    }
}
