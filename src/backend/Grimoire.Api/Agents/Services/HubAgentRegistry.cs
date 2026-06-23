using Grimoire.Api.Agents.Models;
using Grimoire.Api.Shared.Exceptions;

namespace Grimoire.Api.Agents.Services;

/// <summary>
/// Domain service managing agent registration and lifecycle state transitions.
/// Thread-safe via ReaderWriterLockSlim.
/// </summary>
public class HubAgentRegistry
{
    private readonly Dictionary<string, AgentDescriptor> _agents = new();
    private readonly List<object> _domainEvents = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public IReadOnlyDictionary<string, AgentDescriptor> GetRegistrySnapshot()
    {
        _lock.EnterReadLock();
        try { return new Dictionary<string, AgentDescriptor>(_agents); }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    public void RegisterAgent(AgentDescriptor descriptor)
    {
        descriptor.Validate();
        _lock.EnterWriteLock();
        try
        {
            if (_agents.ContainsKey(descriptor.AgentId))
                throw new AgentAlreadyRegisteredException(descriptor.AgentId);
            _agents[descriptor.AgentId] = descriptor;
            _domainEvents.Add(new AgentRegisteredEvent(descriptor.AgentId, descriptor.Name));
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void StartAgent(string agentId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_agents.TryGetValue(agentId, out var descriptor))
                throw new AgentNotFoundException(agentId);
            if (descriptor.Status != AgentStatus.Unregistered)
                throw new InvalidStateTransitionException(descriptor.Status.ToString(), AgentStatus.Running.ToString());
            _agents[agentId] = descriptor with { Status = AgentStatus.Starting };
            _domainEvents.Add(new AgentStartingEvent(agentId));
            _agents[agentId] = _agents[agentId] with { Status = AgentStatus.Running };
            _domainEvents.Add(new AgentRunningEvent(agentId));
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void StopAgent(string agentId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_agents.TryGetValue(agentId, out var descriptor))
                throw new AgentNotFoundException(agentId);
            if (descriptor.Status != AgentStatus.Running)
                throw new InvalidStateTransitionException(descriptor.Status.ToString(), AgentStatus.Stopped.ToString());
            _agents[agentId] = descriptor with { Status = AgentStatus.Stopping };
            _domainEvents.Add(new AgentStoppingEvent(agentId));
            _agents[agentId] = _agents[agentId] with { Status = AgentStatus.Stopped };
            _domainEvents.Add(new AgentStoppedEvent(agentId));
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void MarkAgentFaulted(string agentId, string reason)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_agents.TryGetValue(agentId, out var descriptor))
                throw new AgentNotFoundException(agentId);
            if (descriptor.Status != AgentStatus.Running)
                throw new InvalidStateTransitionException(descriptor.Status.ToString(), AgentStatus.Faulted.ToString());
            _agents[agentId] = descriptor with { Status = AgentStatus.Faulted };
            _domainEvents.Add(new AgentFaultedEvent(agentId, reason));
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void RecordHealthCheck(string agentId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_agents.TryGetValue(agentId, out var descriptor))
                throw new AgentNotFoundException(agentId);
            _agents[agentId] = descriptor with { LastHealthCheckAt = DateTime.UtcNow };
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool ValidateTransition(AgentStatus from, AgentStatus to) =>
        (from, to) switch
        {
            (AgentStatus.Unregistered, AgentStatus.Starting) => true,
            (AgentStatus.Starting, AgentStatus.Running) => true,
            (AgentStatus.Running, AgentStatus.Stopping) => true,
            (AgentStatus.Stopping, AgentStatus.Stopped) => true,
            (AgentStatus.Running, AgentStatus.Faulted) => true,
            _ => false
        };
}

public record AgentRegisteredEvent(string AgentId, string AgentName);
public record AgentStartingEvent(string AgentId);
public record AgentRunningEvent(string AgentId);
public record AgentStoppingEvent(string AgentId);
public record AgentStoppedEvent(string AgentId);
public record AgentFaultedEvent(string AgentId, string Reason);
