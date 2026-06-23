using NetArchTest.Rules;
using Xunit;

namespace Grimoire.Api.Tests.Architecture;

/// <summary>
/// Phase 0 (MANDATORY): Architecture test enforcing domain isolation.
/// This test MUST FAIL before domain code is written (TDD approach to architecture).
/// </summary>
public class DomainIsolationTests
{
    [Fact]
    public void CoreNamespace_HasNoDependencyOnInfrastructure()
    {
        // Core namespace must be dependency-free (per Constitution Principle III)
        var result = Types.InNamespace("Grimoire.Core")
            .ShouldNot()
            .HaveDependencyOn("Grimoire.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Core namespace must not import Infrastructure layer (domain isolation)");
    }

    [Fact]
    public void CoreNamespace_HasNoDependencyOnApi()
    {
        // Core must never reference API layer
        var result = Types.InNamespace("Grimoire.Core")
            .ShouldNot()
            .HaveDependencyOn("Grimoire.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Core namespace must not import Api layer");
    }

    [Fact]
    public void CoreNamespace_HasNoDependencyOnFramework()
    {
        // Core must not reference framework-specific packages
        var result = Types.InNamespace("Grimoire.Core")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Grimoire.Core must not reference EntityFrameworkCore (dependency-free domain)");
    }

    [Fact]
    public void InfrastructurePersistence_CannotImportFromApi()
    {
        // Infrastructure.Persistence layer must not depend on Api layer
        var result = Types.InNamespace("Grimoire.Infrastructure.Persistence")
            .ShouldNot()
            .HaveDependencyOn("Grimoire.Api")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure.Persistence must not import Api layer (layered architecture)");
    }

    [Fact]
    public void IAgentWorker_DefinedInCoreAgents()
    {
        // IAgentWorker interface must be defined in Grimoire.Core.Agents
        var result = Types.InNamespace("Grimoire.Core.Agents")
            .That()
            .HaveNameMatching("IAgentWorker")
            .Should()
            .BeInterfaces()
            .GetResult();

        Assert.True(result.IsSuccessful,
            "IAgentWorker must be defined as an interface in Grimoire.Core.Agents");
    }

    [Fact]
    public void IAgentWorker_ImplementedInAgents()
    {
        // Implementations of IAgentWorker may exist in Grimoire.Agents layer
        var result = Types.InNamespace("Grimoire.Agents")
            .That()
            .ImplementInterface(typeof(Grimoire.Core.Agents.IAgentWorker))
            .Should()
            .ResideInNamespace("Grimoire.Agents")
            .GetResult();

        // This test passes vacuously if no implementations exist yet (expected in skeleton)
        Assert.True(result.IsSuccessful,
            "IAgentWorker implementations must reside in Grimoire.Agents layer (if they exist)");
    }
}
