namespace Grimoire.Api.Core.Exceptions;

using Grimoire.Api.Core.Domain;

/// <summary>
/// Base class for all domain-level exceptions.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when attempting to register an agent with a duplicate AgentId.
/// HTTP 409 Conflict.
/// </summary>
public class AgentAlreadyRegisteredException : DomainException
{
    public string AgentId { get; }

    public AgentAlreadyRegisteredException(string agentId)
        : base($"Agent with ID '{agentId}' is already registered.")
    {
        AgentId = agentId;
    }
}

/// <summary>
/// Thrown when an operation targets an agent that is not registered.
/// HTTP 404 Not Found.
/// </summary>
public class AgentNotFoundException : DomainException
{
    public string AgentId { get; }

    public AgentNotFoundException(string agentId)
        : base($"Agent with ID '{agentId}' not found.")
    {
        AgentId = agentId;
    }
}

/// <summary>
/// Thrown when a state transition is invalid per the agent lifecycle state machine.
/// HTTP 400 Bad Request.
/// </summary>
public class InvalidStateTransitionException : DomainException
{
    public AgentStatus FromState { get; }
    public AgentStatus ToState { get; }

    public InvalidStateTransitionException(AgentStatus from, AgentStatus to)
        : base($"Invalid state transition from {from} to {to}.")
    {
        FromState = from;
        ToState = to;
    }
}
