using System.Data.SQLite;
using Grimoire.Api.Core.Domain;

namespace Grimoire.Api.Infrastructure.Persistence;

/// <summary>
/// Handles SQLite database initialization and state recovery on startup.
/// Creates the schema if it does not exist and rehydrates the in-memory registry.
/// </summary>
public class AgentDbInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<AgentDbInitializer> _logger;

    public AgentDbInitializer(string connectionString, ILogger<AgentDbInitializer> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    /// <summary>
    /// Creates the SQLite database file and applies the initial schema.
    /// The schema SQL uses CREATE TABLE IF NOT EXISTS, so it is safe to run on every startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var connection = new SQLiteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = ReadSchemaSql();

        await using var command = new SQLiteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();

        _logger.LogInformation("sqlite_initialized connectionString={ConnectionString}", _connectionString);
    }

    /// <summary>
    /// Recovers all persisted agent descriptors from the database and re-registers them
    /// in the in-memory registry. Agents that fail re-registration are skipped with a warning.
    /// </summary>
    public async Task RecoverStateAsync(HubAgentRegistry registry)
    {
        var repository = new AgentRepository(_connectionString);
        var (agents, _) = await repository.RecoverStateAsync();

        foreach (var agent in agents)
        {
            try
            {
                registry.RegisterAgent(agent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "sqlite_recovery_agent_skip agentId={AgentId} reason={Reason}",
                    agent.AgentId, ex.Message);
            }
        }

        _logger.LogInformation("sqlite_recovery agents_recovered={Count}", agents.Count);
    }

    private static string ReadSchemaSql()
    {
        var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        var schemaPath = Path.Combine(assemblyDir, "Infrastructure", "Persistence", "InitialSchema.sql");

        if (File.Exists(schemaPath))
            return File.ReadAllText(schemaPath);

        // Fallback: inline schema when file is not present in output directory
        return @"
CREATE TABLE IF NOT EXISTS AgentDescriptors (
    AgentId TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Status INTEGER NOT NULL,
    Capabilities TEXT,
    RegisteredAt TEXT NOT NULL,
    LastHealthCheckAt TEXT
);
CREATE TABLE IF NOT EXISTS AgentJobs (
    JobId TEXT PRIMARY KEY,
    AgentId TEXT NOT NULL,
    Payload TEXT NOT NULL,
    Status INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    StartedAt TEXT,
    CompletedAt TEXT,
    FailedAt TEXT,
    ErrorMessage TEXT,
    FOREIGN KEY (AgentId) REFERENCES AgentDescriptors(AgentId) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_AgentJobs_AgentId ON AgentJobs(AgentId);
CREATE INDEX IF NOT EXISTS idx_AgentJobs_Status ON AgentJobs(Status);
CREATE INDEX IF NOT EXISTS idx_AgentJobs_CreatedAt ON AgentJobs(CreatedAt);";
    }
}
