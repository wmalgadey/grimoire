using NetArchTest.Rules;
using Xunit;

namespace Grimoire.Api.Tests.Architecture;

/// <summary>
/// ADR-009: Enforce domain-driven code organization (Screaming Architecture).
/// Domains: Agents, Hubs, Channels, Shared. No circular dependencies. No layer-based namespaces.
/// This test MUST FAIL before domain code is written (TDD approach to architecture).
/// </summary>
public class DomainIsolationTests
{
    [Fact]
    public void AgentsDomain_MustNotDependOnHubs()
    {
        var result = Types.InNamespace("Grimoire.Api.Agents")
            .Should()
            .NotHaveDependencyOn("Grimoire.Api.Hubs")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Api.Agents must not depend on Grimoire.Api.Hubs (no circular domain dependencies)");
    }

    [Fact]
    public void AgentsDomain_MustNotDependOnChannels()
    {
        var result = Types.InNamespace("Grimoire.Api.Agents")
            .Should()
            .NotHaveDependencyOn("Grimoire.Api.Channels")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Api.Agents must not depend on Grimoire.Api.Channels (no circular domain dependencies)");
    }

    [Fact]
    public void ChannelsDomain_MustNotDependOnHubs()
    {
        var result = Types.InNamespace("Grimoire.Api.Channels")
            .Should()
            .NotHaveDependencyOn("Grimoire.Api.Hubs")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Api.Channels must not depend on Grimoire.Api.Hubs (no circular domain dependencies)");
    }

    [Fact]
    public void ChannelsDomain_MustNotDependOnAgents()
    {
        var result = Types.InNamespace("Grimoire.Api.Channels")
            .Should()
            .NotHaveDependencyOn("Grimoire.Api.Agents")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Api.Channels must not depend on Grimoire.Api.Agents (no circular domain dependencies)");
    }

    [Fact]
    public void SharedDomain_MustNotDependOnAgents()
    {
        var result = Types.InNamespace("Grimoire.Api.Shared")
            .Should()
            .NotHaveDependencyOn("Grimoire.Api.Agents")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Api.Shared must not depend on domain-specific code");
    }

    [Fact]
    public void SharedDomain_MustNotDependOnHubs()
    {
        var result = Types.InNamespace("Grimoire.Api.Shared")
            .Should()
            .NotHaveDependencyOn("Grimoire.Api.Hubs")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Api.Shared must not depend on domain-specific code");
    }

    [Fact]
    public void HubsDomain_MustNotDependOnAgentConcretes()
    {
        var result = Types.InNamespace("Grimoire.Api.Hubs")
            .Should()
            .NotHaveDependencyOn("Grimoire.Api.Agents.Endpoints")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Api.Hubs must not depend on Grimoire.Api.Agents.Endpoints (communicate via interfaces only)");
    }

    [Fact]
    public void OldLayerNamespace_Api_MustNotExist()
    {
        var types = Types.InNamespace("Grimoire.Api.Api").GetTypes();
        Assert.Empty(types);
    }

    [Fact]
    public void OldLayerNamespace_Core_MustNotExist()
    {
        var types = Types.InNamespace("Grimoire.Api.Core").GetTypes();
        Assert.Empty(types);
    }

    [Fact]
    public void OldLayerNamespace_Infrastructure_MustNotExist()
    {
        var types = Types.InNamespace("Grimoire.Api.Infrastructure").GetTypes();
        Assert.Empty(types);
    }
}
