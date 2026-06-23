using System.Text.Json.Serialization;

namespace Grimoire.Api.Agents.Models;

public record AgentHealthStatus
{
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    [JsonPropertyName("isHealthy")]
    public required bool IsHealthy { get; init; }

    [JsonPropertyName("checkedAt")]
    public required DateTime CheckedAt { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
