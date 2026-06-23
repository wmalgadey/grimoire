namespace Grimoire.Api.Shared.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>HTTP 409 Conflict.</summary>
public class AgentAlreadyRegisteredException : DomainException
{
    public string AgentId { get; }

    public AgentAlreadyRegisteredException(string agentId)
        : base($"Agent with ID '{agentId}' is already registered.")
    {
        AgentId = agentId;
    }
}

/// <summary>HTTP 404 Not Found.</summary>
public class AgentNotFoundException : DomainException
{
    public string AgentId { get; }

    public AgentNotFoundException(string agentId)
        : base($"Agent with ID '{agentId}' not found.")
    {
        AgentId = agentId;
    }
}

/// <summary>HTTP 400 Bad Request.</summary>
public class InvalidStateTransitionException : DomainException
{
    public string FromState { get; }
    public string ToState { get; }

    public InvalidStateTransitionException(string from, string to)
        : base($"Invalid state transition from {from} to {to}.")
    {
        FromState = from;
        ToState = to;
    }
}
