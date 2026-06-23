using System.Text.Json.Serialization;

namespace Grimoire.Api.Core.Domain;

/// <summary>
/// Immutable value object representing an agent's public metadata.
/// </summary>
public record AgentDescriptor
{
    /// <summary>
    /// Unique identifier for this agent (alphanumeric + hyphens).
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    /// <summary>
    /// Human-readable agent name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    [JsonPropertyName("status")]
    public required AgentStatus Status { get; init; }

    /// <summary>
    /// List of capabilities this agent provides (e.g., ["ingest", "query"]).
    /// </summary>
    [JsonPropertyName("capabilities")]
    public string[]? Capabilities { get; init; }

    /// <summary>
    /// Timestamp when this agent was registered.
    /// </summary>
    [JsonPropertyName("registeredAt")]
    public required DateTime RegisteredAt { get; init; }

    /// <summary>
    /// Timestamp of the last health check, or null if never checked.
    /// </summary>
    [JsonPropertyName("lastHealthCheckAt")]
    public DateTime? LastHealthCheckAt { get; init; }

    /// <summary>
    /// Validates agent descriptor constraints:
    /// - AgentId is non-empty and contains only alphanumeric + hyphens
    /// - Name is non-empty
    /// - Capabilities (if present) are non-empty strings
    /// </summary>
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
