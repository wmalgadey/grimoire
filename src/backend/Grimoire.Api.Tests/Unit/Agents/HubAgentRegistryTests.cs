using Grimoire.Api.Agents.Services;
using Grimoire.Api.Agents.Models;
using Grimoire.Api.Shared.Exceptions;
using Xunit;

namespace Grimoire.Api.Tests.Unit.Agents;

public class HubAgentRegistryTests
{
    // -------------------------------------------------------------------------
    // Helper factory methods
    // -------------------------------------------------------------------------

    private static AgentDescriptor MakeDescriptor(string agentId = "agent-01", string name = "Test Agent") =>
        new AgentDescriptor
        {
            AgentId = agentId,
            Name = name,
            Status = AgentStatus.Unregistered,
            Capabilities = Array.Empty<string>(),
            RegisteredAt = DateTime.UtcNow
        };

    // -------------------------------------------------------------------------
    // RegisterAgent
    // -------------------------------------------------------------------------

    [Fact]
    public void RegisterAgent_NewAgent_AppearsInSnapshot()
    {
        var registry = new HubAgentRegistry();
        var descriptor = MakeDescriptor();

        registry.RegisterAgent(descriptor);

        var snapshot = registry.GetRegistrySnapshot();
        Assert.True(snapshot.ContainsKey("agent-01"));
        Assert.Equal("agent-01", snapshot["agent-01"].AgentId);
    }

    [Fact]
    public void RegisterAgent_NewAgent_EmitsAgentRegisteredEvent()
    {
        var registry = new HubAgentRegistry();
        var descriptor = MakeDescriptor();

        registry.RegisterAgent(descriptor);

        var events = registry.DomainEvents;
        Assert.Single(events);
        var evt = Assert.IsType<AgentRegisteredEvent>(events[0]);
        Assert.Equal("agent-01", evt.AgentId);
        Assert.Equal("Test Agent", evt.AgentName);
    }

    [Fact]
    public void RegisterAgent_DuplicateAgentId_ThrowsAgentAlreadyRegisteredException()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());

        var ex = Assert.Throws<AgentAlreadyRegisteredException>(() =>
            registry.RegisterAgent(MakeDescriptor()));

        Assert.Equal("agent-01", ex.AgentId);
    }

    [Fact]
    public void RegisterAgent_MultipleDistinctAgents_AllAppearInSnapshot()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor("agent-01", "First"));
        registry.RegisterAgent(MakeDescriptor("agent-02", "Second"));
        registry.RegisterAgent(MakeDescriptor("agent-03", "Third"));

        var snapshot = registry.GetRegistrySnapshot();

        Assert.Equal(3, snapshot.Count);
        Assert.True(snapshot.ContainsKey("agent-01"));
        Assert.True(snapshot.ContainsKey("agent-02"));
        Assert.True(snapshot.ContainsKey("agent-03"));
    }

    // -------------------------------------------------------------------------
    // GetRegistrySnapshot
    // -------------------------------------------------------------------------

    [Fact]
    public void GetRegistrySnapshot_EmptyRegistry_ReturnsEmptyDictionary()
    {
        var registry = new HubAgentRegistry();
        Assert.Empty(registry.GetRegistrySnapshot());
    }

    [Fact]
    public void GetRegistrySnapshot_IsReadOnly_CannotMutateExternally()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());

        var snapshot = registry.GetRegistrySnapshot();

        // The snapshot is a read-only view; casting should fail at runtime
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, AgentDescriptor>>(snapshot);
    }

    // -------------------------------------------------------------------------
    // StartAgent
    // -------------------------------------------------------------------------

    [Fact]
    public void StartAgent_RegisteredUnregisteredAgent_TransitionsToRunning()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());

        registry.StartAgent("agent-01");

        var snapshot = registry.GetRegistrySnapshot();
        Assert.Equal(AgentStatus.Running, snapshot["agent-01"].Status);
    }

    [Fact]
    public void StartAgent_EmitsStartingThenRunningEvents()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.ClearDomainEvents();

        registry.StartAgent("agent-01");

        var events = registry.DomainEvents;
        Assert.Equal(2, events.Count);
        Assert.IsType<AgentStartingEvent>(events[0]);
        Assert.IsType<AgentRunningEvent>(events[1]);
        Assert.Equal("agent-01", ((AgentStartingEvent)events[0]).AgentId);
        Assert.Equal("agent-01", ((AgentRunningEvent)events[1]).AgentId);
    }

    [Fact]
    public void StartAgent_NonExistentAgent_ThrowsAgentNotFoundException()
    {
        var registry = new HubAgentRegistry();

        var ex = Assert.Throws<AgentNotFoundException>(() =>
            registry.StartAgent("does-not-exist"));

        Assert.Equal("does-not-exist", ex.AgentId);
    }

    [Fact]
    public void StartAgent_AgentAlreadyRunning_ThrowsInvalidStateTransitionException()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.StartAgent("agent-01");  // now Running

        var ex = Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StartAgent("agent-01"));

        Assert.Equal(AgentStatus.Running.ToString(), ex.FromState);
        Assert.Equal(AgentStatus.Running.ToString(), ex.ToState);
    }

    [Fact]
    public void StartAgent_AgentAlreadyStopped_ThrowsInvalidStateTransitionException()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.StartAgent("agent-01");
        registry.StopAgent("agent-01");  // now Stopped

        Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StartAgent("agent-01"));
    }

    // -------------------------------------------------------------------------
    // StopAgent
    // -------------------------------------------------------------------------

    [Fact]
    public void StopAgent_RunningAgent_TransitionsToStopped()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.StartAgent("agent-01");

        registry.StopAgent("agent-01");

        Assert.Equal(AgentStatus.Stopped, registry.GetRegistrySnapshot()["agent-01"].Status);
    }

    [Fact]
    public void StopAgent_EmitsStoppingThenStoppedEvents()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.StartAgent("agent-01");
        registry.ClearDomainEvents();

        registry.StopAgent("agent-01");

        var events = registry.DomainEvents;
        Assert.Equal(2, events.Count);
        Assert.IsType<AgentStoppingEvent>(events[0]);
        Assert.IsType<AgentStoppedEvent>(events[1]);
        Assert.Equal("agent-01", ((AgentStoppingEvent)events[0]).AgentId);
        Assert.Equal("agent-01", ((AgentStoppedEvent)events[1]).AgentId);
    }

    [Fact]
    public void StopAgent_NonExistentAgent_ThrowsAgentNotFoundException()
    {
        var registry = new HubAgentRegistry();

        Assert.Throws<AgentNotFoundException>(() =>
            registry.StopAgent("does-not-exist"));
    }

    [Fact]
    public void StopAgent_AgentNotRunning_ThrowsInvalidStateTransitionException()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        // Agent is Unregistered after registration; not Running

        Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StopAgent("agent-01"));
    }

    // -------------------------------------------------------------------------
    // MarkAgentFaulted
    // -------------------------------------------------------------------------

    [Fact]
    public void MarkAgentFaulted_RunningAgent_SetsFaultedStatus()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.StartAgent("agent-01");

        registry.MarkAgentFaulted("agent-01", "Unhandled exception in worker");

        Assert.Equal(AgentStatus.Faulted, registry.GetRegistrySnapshot()["agent-01"].Status);
    }

    [Fact]
    public void MarkAgentFaulted_EmitsAgentFaultedEventWithReason()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.StartAgent("agent-01");
        registry.ClearDomainEvents();

        registry.MarkAgentFaulted("agent-01", "Out of memory");

        var events = registry.DomainEvents;
        Assert.Single(events);
        var evt = Assert.IsType<AgentFaultedEvent>(events[0]);
        Assert.Equal("agent-01", evt.AgentId);
        Assert.Equal("Out of memory", evt.Reason);
    }

    [Fact]
    public void MarkAgentFaulted_NonExistentAgent_ThrowsAgentNotFoundException()
    {
        var registry = new HubAgentRegistry();

        Assert.Throws<AgentNotFoundException>(() =>
            registry.MarkAgentFaulted("ghost", "reason"));
    }

    [Fact]
    public void MarkAgentFaulted_AlreadyFaultedAgent_ThrowsInvalidStateTransition()
    {
        // Faulted is terminal; cannot transition from Faulted to Faulted
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        registry.StartAgent("agent-01");
        registry.MarkAgentFaulted("agent-01", "First fault");

        // Attempting to fault an already-faulted agent should throw
        Assert.Throws<InvalidStateTransitionException>(() =>
            registry.MarkAgentFaulted("agent-01", "Second fault"));
    }

    // -------------------------------------------------------------------------
    // ClearDomainEvents
    // -------------------------------------------------------------------------

    [Fact]
    public void ClearDomainEvents_AfterEvents_EmptiesList()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        Assert.NotEmpty(registry.DomainEvents);

        registry.ClearDomainEvents();

        Assert.Empty(registry.DomainEvents);
    }

    // -------------------------------------------------------------------------
    // ValidateTransition
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(AgentStatus.Unregistered, AgentStatus.Starting, true)]
    [InlineData(AgentStatus.Starting,     AgentStatus.Running,   true)]
    [InlineData(AgentStatus.Running,      AgentStatus.Stopping,  true)]
    [InlineData(AgentStatus.Stopping,     AgentStatus.Stopped,   true)]
    [InlineData(AgentStatus.Running,      AgentStatus.Faulted,   true)]
    // Invalid
    [InlineData(AgentStatus.Stopped,      AgentStatus.Running,   false)]
    [InlineData(AgentStatus.Faulted,      AgentStatus.Running,   false)]
    [InlineData(AgentStatus.Unregistered, AgentStatus.Running,   false)]
    [InlineData(AgentStatus.Running,      AgentStatus.Unregistered, false)]
    [InlineData(AgentStatus.Stopped,      AgentStatus.Starting,  false)]
    public void ValidateTransition_KnownPairs_ReturnsExpectedResult(
        AgentStatus from, AgentStatus to, bool expected)
    {
        var registry = new HubAgentRegistry();
        Assert.Equal(expected, registry.ValidateTransition(from, to));
    }
}
