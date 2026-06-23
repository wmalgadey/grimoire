namespace Grimoire.Api.Agents.Models;

/// <summary>
/// Agent lifecycle states: Unregistered → Starting → Running → Stopping → Stopped
/// or Running → Faulted (terminal).
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
