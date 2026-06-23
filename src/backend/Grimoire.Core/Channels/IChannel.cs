namespace Grimoire.Core.Channels;

/// <summary>
/// Unified abstraction for all output channels (ADR-004).
/// Implementations route agent results to specific protocols (SignalR, Telegram Bot API, etc.)
/// without the orchestrator having knowledge of the underlying transport.
/// </summary>
public interface IChannel
{
    /// <summary>Unique identifier for this channel instance.</summary>
    string ChannelId { get; }

    /// <summary>Deliver a message payload to the channel's endpoint.</summary>
    Task SendAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>Gracefully close the channel connection. Idempotent.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
