namespace Grimoire.Api.Core.Domain;

/// <summary>
/// Agent lifecycle states per state machine: Unregistered → Starting → Running → Stopping → Stopped
/// or Running → Faulted (terminal state).
/// </summary>
public enum AgentStatus
{
    Unregistered = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Stopped = 4,
    Faulted = 5
}
