using Grimoire.Api.Core.Domain;
using Grimoire.Api.Core.Exceptions;
using Xunit;

namespace Grimoire.Api.Tests.Unit.Domain;

/// <summary>
/// Tests for the agent lifecycle state machine, covering valid and invalid
/// transitions both via ValidateTransition() and the real registry operations.
/// </summary>
public class AgentLifecycleStateTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AgentDescriptor MakeDescriptor(string agentId = "agent-01") =>
        new AgentDescriptor
        {
            AgentId = agentId,
            Name = "Lifecycle Test Agent",
            Status = AgentStatus.Unregistered,
            Capabilities = Array.Empty<string>(),
            RegisteredAt = DateTime.UtcNow
        };

    private static HubAgentRegistry RegistryWithRunningAgent(string agentId = "agent-01")
    {
        var r = new HubAgentRegistry();
        r.RegisterAgent(MakeDescriptor(agentId));
        r.StartAgent(agentId);
        return r;
    }

    // -------------------------------------------------------------------------
    // ValidateTransition — every allowed pair
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateTransition_UnregisteredToStarting_IsValid()
    {
        Assert.True(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Unregistered, AgentStatus.Starting));
    }

    [Fact]
    public void ValidateTransition_StartingToRunning_IsValid()
    {
        Assert.True(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Starting, AgentStatus.Running));
    }

    [Fact]
    public void ValidateTransition_RunningToStopping_IsValid()
    {
        Assert.True(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Running, AgentStatus.Stopping));
    }

    [Fact]
    public void ValidateTransition_StoppingToStopped_IsValid()
    {
        Assert.True(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Stopping, AgentStatus.Stopped));
    }

    [Fact]
    public void ValidateTransition_RunningToFaulted_IsValid()
    {
        Assert.True(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Running, AgentStatus.Faulted));
    }

    // -------------------------------------------------------------------------
    // ValidateTransition — disallowed pairs
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateTransition_StoppedToRunning_IsInvalid()
    {
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Stopped, AgentStatus.Running));
    }

    [Fact]
    public void ValidateTransition_FaultedToRunning_IsInvalid()
    {
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Faulted, AgentStatus.Running));
    }

    [Fact]
    public void ValidateTransition_UnregisteredToRunning_IsInvalid()
    {
        // Skipping Starting step is not allowed
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Unregistered, AgentStatus.Running));
    }

    [Fact]
    public void ValidateTransition_RunningToUnregistered_IsInvalid()
    {
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Running, AgentStatus.Unregistered));
    }

    [Fact]
    public void ValidateTransition_StoppedToStarting_IsInvalid()
    {
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Stopped, AgentStatus.Starting));
    }

    [Fact]
    public void ValidateTransition_FaultedToStopped_IsInvalid()
    {
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Faulted, AgentStatus.Stopped));
    }

    [Fact]
    public void ValidateTransition_StartingToStopped_IsInvalid()
    {
        // Skipping Running and Stopping steps
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Starting, AgentStatus.Stopped));
    }

    [Fact]
    public void ValidateTransition_SameState_IsInvalid()
    {
        // Self-transition is not defined in the state machine
        Assert.False(new HubAgentRegistry().ValidateTransition(
            AgentStatus.Running, AgentStatus.Running));
    }

    // -------------------------------------------------------------------------
    // Real transitions via registry operations
    // -------------------------------------------------------------------------

    [Fact]
    public void Lifecycle_UnregisteredToRunning_ViaStartAgent()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());
        // After registration status is Unregistered per the descriptor
        Assert.Equal(AgentStatus.Unregistered, registry.GetRegistrySnapshot()["agent-01"].Status);

        registry.StartAgent("agent-01");

        Assert.Equal(AgentStatus.Running, registry.GetRegistrySnapshot()["agent-01"].Status);
    }

    [Fact]
    public void Lifecycle_RunningToStopped_ViaStopAgent()
    {
        var registry = RegistryWithRunningAgent();
        Assert.Equal(AgentStatus.Running, registry.GetRegistrySnapshot()["agent-01"].Status);

        registry.StopAgent("agent-01");

        Assert.Equal(AgentStatus.Stopped, registry.GetRegistrySnapshot()["agent-01"].Status);
    }

    [Fact]
    public void Lifecycle_RunningToFaulted_ViaMarkAgentFaulted()
    {
        var registry = RegistryWithRunningAgent();

        registry.MarkAgentFaulted("agent-01", "health check failed");

        Assert.Equal(AgentStatus.Faulted, registry.GetRegistrySnapshot()["agent-01"].Status);
    }

    [Fact]
    public void Lifecycle_FullHappyPath_UnregisteredStartRunningStopStopped()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());

        registry.StartAgent("agent-01");
        Assert.Equal(AgentStatus.Running, registry.GetRegistrySnapshot()["agent-01"].Status);

        registry.StopAgent("agent-01");
        Assert.Equal(AgentStatus.Stopped, registry.GetRegistrySnapshot()["agent-01"].Status);
    }

    // -------------------------------------------------------------------------
    // Invalid transitions enforced by registry
    // -------------------------------------------------------------------------

    [Fact]
    public void StartAgent_OnStoppedAgent_ThrowsInvalidStateTransitionException()
    {
        var registry = RegistryWithRunningAgent();
        registry.StopAgent("agent-01");  // now Stopped

        Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StartAgent("agent-01"));
    }

    [Fact]
    public void StartAgent_OnFaultedAgent_ThrowsInvalidStateTransitionException()
    {
        var registry = RegistryWithRunningAgent();
        registry.MarkAgentFaulted("agent-01", "fault");  // now Faulted

        Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StartAgent("agent-01"));
    }

    [Fact]
    public void StopAgent_OnUnregisteredAgent_ThrowsInvalidStateTransitionException()
    {
        var registry = new HubAgentRegistry();
        registry.RegisterAgent(MakeDescriptor());  // status Unregistered, not Running

        Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StopAgent("agent-01"));
    }

    [Fact]
    public void StopAgent_OnFaultedAgent_ThrowsInvalidStateTransitionException()
    {
        var registry = RegistryWithRunningAgent();
        registry.MarkAgentFaulted("agent-01", "fault");  // now Faulted

        Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StopAgent("agent-01"));
    }

    // -------------------------------------------------------------------------
    // InvalidStateTransitionException carries state information
    // -------------------------------------------------------------------------

    [Fact]
    public void InvalidStateTransitionException_ContainsFromAndToState()
    {
        var registry = RegistryWithRunningAgent();
        registry.StopAgent("agent-01");  // Stopped

        var ex = Assert.Throws<InvalidStateTransitionException>(() =>
            registry.StartAgent("agent-01"));

        Assert.Equal(AgentStatus.Stopped, ex.FromState);
        Assert.Equal(AgentStatus.Running, ex.ToState);
    }
}
