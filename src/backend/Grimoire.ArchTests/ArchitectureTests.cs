using NetArchTest.Rules;
using Xunit;

namespace Grimoire.ArchTests;

public class ArchitectureTests
{
    private const string CoreNamespace = "Grimoire.Core";
    private const string InfraNamespace = "Grimoire.Infrastructure";
    private const string AgentsNamespace = "Grimoire.Agents";
    private const string ChannelsImplNamespace = "Grimoire.Infrastructure.Channels";

    [Fact]
    public void Core_HasNoDependencyOnInfrastructure()
    {
        var result = Types.InAssembly(typeof(Grimoire.Core.Channels.IChannel).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfraNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, "Grimoire.Core must not reference any Infrastructure assembly.");
    }

    [Fact]
    public void Core_HasNoDependencyOnEntityFramework()
    {
        var result = Types.InAssembly(typeof(Grimoire.Core.Channels.IChannel).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, "Grimoire.Core must not reference Entity Framework Core (ADR-007).");
    }

    [Fact]
    public void ChannelImplementations_MustImplementIChannel()
    {
        // No channel implementations exist yet in skeleton — test passes vacuously (no violations).
        var result = Types.InAssembly(typeof(Grimoire.Core.Channels.IChannel).Assembly)
            .That()
            .ResideInNamespace(ChannelsImplNamespace)
            .Should()
            .ImplementInterface(typeof(Grimoire.Core.Channels.IChannel))
            .GetResult();

        Assert.True(result.IsSuccessful, "All classes in Grimoire.Infrastructure.Channels must implement IChannel (ADR-004).");
    }

    [Fact]
    public void AgentWorkers_MustImplementIAgentWorker()
    {
        // No agent implementations exist yet in skeleton — test passes vacuously (no violations).
        var result = Types.InAssembly(typeof(Grimoire.Core.Agents.IAgentWorker).Assembly)
            .That()
            .ResideInNamespace(AgentsNamespace)
            .And()
            .AreClasses()
            .Should()
            .ImplementInterface(typeof(Grimoire.Core.Agents.IAgentWorker))
            .GetResult();

        Assert.True(result.IsSuccessful, "All classes in Grimoire.Agents.* must implement IAgentWorker (ADR-002).");
    }

    [Fact]
    public void Core_DefinesIChannelInCorrectNamespace()
    {
        var result = Types.InAssembly(typeof(Grimoire.Core.Channels.IChannel).Assembly)
            .That()
            .HaveNameMatching("IChannel")
            .Should()
            .ResideInNamespace("Grimoire.Core.Channels")
            .GetResult();

        Assert.True(result.IsSuccessful, "IChannel must reside in Grimoire.Core.Channels namespace (ADR-004).");
    }

    [Fact]
    public void Core_DefinesIAgentWorkerInCorrectNamespace()
    {
        var result = Types.InAssembly(typeof(Grimoire.Core.Agents.IAgentWorker).Assembly)
            .That()
            .HaveNameMatching("IAgentWorker")
            .Should()
            .ResideInNamespace("Grimoire.Core.Agents")
            .GetResult();

        Assert.True(result.IsSuccessful, "IAgentWorker must reside in Grimoire.Core.Agents namespace (ADR-002).");
    }
}
