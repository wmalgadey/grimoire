using System.Text.Json.Serialization;

namespace Grimoire.Api.Agents.Models;

public record AgentDescriptor
{
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required AgentStatus Status { get; init; }

    [JsonPropertyName("capabilities")]
    public string[]? Capabilities { get; init; }

    [JsonPropertyName("registeredAt")]
    public required DateTime RegisteredAt { get; init; }

    [JsonPropertyName("lastHealthCheckAt")]
    public DateTime? LastHealthCheckAt { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentId))
            throw new ArgumentException("AgentId must not be empty.", nameof(AgentId));

        if (!System.Text.RegularExpressions.Regex.IsMatch(AgentId, @"^[a-zA-Z0-9-]+$"))
            throw new ArgumentException("AgentId must contain only alphanumeric characters and hyphens.", nameof(AgentId));

        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Name must not be empty.", nameof(Name));

        if (Capabilities != null)
        {
            foreach (var cap in Capabilities)
            {
                if (string.IsNullOrWhiteSpace(cap))
                    throw new ArgumentException("Capabilities must not contain empty strings.", nameof(Capabilities));
            }
        }
    }
}
