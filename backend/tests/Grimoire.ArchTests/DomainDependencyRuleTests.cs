using NetArchTest.Rules;

namespace Grimoire.ArchTests;

public class DomainDependencyRuleTests
{
    [Fact]
    public void Domain_Must_Not_Depend_On_Hub()
    {
        var result = Types.InAssembly(typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Grimoire.Hub")
            .GetResult();

        Assert.True(result.IsSuccessful, "Grimoire.Domain must not depend on Grimoire.Hub.");
    }

    [Fact]
    public void Domain_Must_Not_Depend_On_IngestAgent()
    {
        var result = Types.InAssembly(typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Grimoire.IngestAgent")
            .GetResult();

        Assert.True(result.IsSuccessful, "Grimoire.Domain must not depend on Grimoire.IngestAgent.");
    }

    [Fact]
    public void Domain_Must_Not_Depend_On_AspNetCore()
    {
        var result = Types.InAssembly(typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, "Grimoire.Domain must not depend on Microsoft.AspNetCore.*.");
    }

    [Fact]
    public void Domain_Must_Not_Depend_On_Sqlite()
    {
        var result = Types.InAssembly(typeof(Grimoire.Domain.DomainAssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Data.Sqlite")
            .GetResult();

        Assert.True(result.IsSuccessful, "Grimoire.Domain must not depend on Microsoft.Data.Sqlite.");
    }
}
