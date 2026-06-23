using System.Text.Json.Serialization;

namespace Grimoire.Api.Core.Domain;

/// <summary>
/// Immutable value object capturing an agent's health status at a point in time.
/// </summary>
public record AgentHealthStatus
{
    /// <summary>
    /// Agent ID this health check is for.
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    /// <summary>
    /// Whether the agent is healthy (true) or faulted (false).
    /// </summary>
    [JsonPropertyName("isHealthy")]
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Timestamp when this health check was performed.
    /// </summary>
    [JsonPropertyName("checkedAt")]
    public required DateTime CheckedAt { get; init; }

    /// <summary>
    /// Optional message describing health status (e.g., "timeout", "connection refused").
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
