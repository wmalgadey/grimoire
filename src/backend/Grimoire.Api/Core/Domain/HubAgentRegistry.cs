namespace Grimoire.Api.Core.Domain;

using Grimoire.Api.Core.Exceptions;

/// <summary>
/// Domain service (dependency-free) managing agent registration and lifecycle state transitions.
/// Enforces the agent lifecycle state machine and emits domain events.
/// </summary>
public class HubAgentRegistry
{
    private readonly Dictionary<string, AgentDescriptor> _agents = new();
    private readonly List<object> _domainEvents = new();

    /// <summary>
    /// Gets all currently registered agents.
    /// </summary>
    public IReadOnlyDictionary<string, AgentDescriptor> GetRegistrySnapshot() => _agents.AsReadOnly();

    /// <summary>
    /// Gets all domain events emitted during this session.
    /// </summary>
    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Clears the domain events list (typically called after persisting events).
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Registers a new agent in the registry.
    /// Throws AgentAlreadyRegisteredException if agent with this ID already exists.
    /// </summary>
    public void RegisterAgent(AgentDescriptor descriptor)
    {
        descriptor.Validate();

        if (_agents.ContainsKey(descriptor.AgentId))
            throw new AgentAlreadyRegisteredException(descriptor.AgentId);

        _agents[descriptor.AgentId] = descriptor;
        _domainEvents.Add(new AgentRegisteredEvent(descriptor.AgentId, descriptor.Name));
    }

    /// <summary>
    /// Transitions an agent to Starting → Running state.
    /// Throws AgentNotFoundException if agent not registered.
    /// Throws InvalidStateTransitionException if current state doesn't allow start.
    /// </summary>
    public void StartAgent(string agentId)
    {
        if (!_agents.TryGetValue(agentId, out var descriptor))
            throw new AgentNotFoundException(agentId);

        if (descriptor.Status != AgentStatus.Unregistered)
            throw new InvalidStateTransitionException(descriptor.Status, AgentStatus.Running);

        _agents[agentId] = descriptor with
        {
            Status = AgentStatus.Starting
        };
        _domainEvents.Add(new AgentStartingEvent(agentId));

        _agents[agentId] = _agents[agentId] with
        {
            Status = AgentStatus.Running
        };
        _domainEvents.Add(new AgentRunningEvent(agentId));
    }

    /// <summary>
    /// Transitions an agent to Stopping → Stopped state.
    /// Throws AgentNotFoundException if agent not registered.
    /// Throws InvalidStateTransitionException if current state doesn't allow stop.
    /// </summary>
    public void StopAgent(string agentId)
    {
        if (!_agents.TryGetValue(agentId, out var descriptor))
            throw new AgentNotFoundException(agentId);

        if (descriptor.Status != AgentStatus.Running)
            throw new InvalidStateTransitionException(descriptor.Status, AgentStatus.Stopped);

        _agents[agentId] = descriptor with
        {
            Status = AgentStatus.Stopping
        };
        _domainEvents.Add(new AgentStoppingEvent(agentId));

        _agents[agentId] = _agents[agentId] with
        {
            Status = AgentStatus.Stopped
        };
        _domainEvents.Add(new AgentStoppedEvent(agentId));
    }

    /// <summary>
    /// Marks an agent as faulted (terminal state).
    /// Called when health check detects the agent is unhealthy.
    /// Throws AgentNotFoundException if agent not registered.
    /// </summary>
    public void MarkAgentFaulted(string agentId, string reason)
    {
        if (!_agents.TryGetValue(agentId, out var descriptor))
            throw new AgentNotFoundException(agentId);

        _agents[agentId] = descriptor with
        {
            Status = AgentStatus.Faulted
        };
        _domainEvents.Add(new AgentFaultedEvent(agentId, reason));
    }

    /// <summary>
    /// Updates last health check timestamp for an agent.
    /// </summary>
    public void RecordHealthCheck(string agentId)
    {
        if (!_agents.TryGetValue(agentId, out var descriptor))
            throw new AgentNotFoundException(agentId);

        _agents[agentId] = descriptor with
        {
            LastHealthCheckAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Validates whether a state transition is legal.
    /// </summary>
    public bool ValidateTransition(AgentStatus from, AgentStatus to)
    {
        return (from, to) switch
        {
            (AgentStatus.Unregistered, AgentStatus.Starting) => true,
            (AgentStatus.Starting, AgentStatus.Running) => true,
            (AgentStatus.Running, AgentStatus.Stopping) => true,
            (AgentStatus.Stopping, AgentStatus.Stopped) => true,
            (AgentStatus.Running, AgentStatus.Faulted) => true,
            _ => false
        };
    }
}

// Domain Events
public record AgentRegisteredEvent(string AgentId, string AgentName);
public record AgentStartingEvent(string AgentId);
public record AgentRunningEvent(string AgentId);
public record AgentStoppingEvent(string AgentId);
public record AgentStoppedEvent(string AgentId);
public record AgentFaultedEvent(string AgentId, string Reason);
