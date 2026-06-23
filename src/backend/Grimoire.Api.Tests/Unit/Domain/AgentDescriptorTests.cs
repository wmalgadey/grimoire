using Grimoire.Api.Agents.Models;
using Xunit;

namespace Grimoire.Api.Tests.Unit.Domain;

public class AgentDescriptorTests
{
    // -------------------------------------------------------------------------
    // Helper: build a valid descriptor in one place so tests don't duplicate setup
    // -------------------------------------------------------------------------
    private static AgentDescriptor ValidDescriptor(string agentId = "agent-01", string name = "Test Agent") =>
        new AgentDescriptor
        {
            AgentId = agentId,
            Name = name,
            Status = AgentStatus.Unregistered,
            Capabilities = new[] { "ingest" },
            RegisteredAt = DateTime.UtcNow
        };

    // -------------------------------------------------------------------------
    // Validate() — happy path
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("a")]
    [InlineData("agent-01")]
    [InlineData("AGENT")]
    [InlineData("abc123")]
    [InlineData("my-super-agent-99")]
    public void Validate_ValidAgentId_DoesNotThrow(string agentId)
    {
        var descriptor = ValidDescriptor(agentId: agentId);
        // Should complete without any exception
        descriptor.Validate();
    }

    [Fact]
    public void Validate_AllRequiredFieldsPresent_DoesNotThrow()
    {
        var descriptor = ValidDescriptor();
        descriptor.Validate();
    }

    [Fact]
    public void Validate_NullCapabilities_DoesNotThrow()
    {
        var descriptor = new AgentDescriptor
        {
            AgentId = "agent-01",
            Name = "Test Agent",
            Status = AgentStatus.Unregistered,
            Capabilities = null,
            RegisteredAt = DateTime.UtcNow
        };
        descriptor.Validate();
    }

    [Fact]
    public void Validate_MultipleCapabilities_DoesNotThrow()
    {
        var descriptor = new AgentDescriptor
        {
            AgentId = "agent-01",
            Name = "Test Agent",
            Status = AgentStatus.Unregistered,
            Capabilities = new[] { "ingest", "query", "summarise" },
            RegisteredAt = DateTime.UtcNow
        };
        descriptor.Validate();
    }

    // -------------------------------------------------------------------------
    // Validate() — AgentId constraints
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyAgentId_ThrowsArgumentException()
    {
        var descriptor = ValidDescriptor(agentId: "");
        var ex = Assert.Throws<ArgumentException>(() => descriptor.Validate());
        Assert.Contains("AgentId", ex.Message);
    }

    [Fact]
    public void Validate_WhitespaceAgentId_ThrowsArgumentException()
    {
        var descriptor = ValidDescriptor(agentId: "   ");
        Assert.Throws<ArgumentException>(() => descriptor.Validate());
    }

    [Theory]
    [InlineData("agent_01")]   // underscore not allowed
    [InlineData("agent.01")]   // dot not allowed
    [InlineData("agent 01")]   // space not allowed
    [InlineData("agent@01")]   // @ not allowed
    [InlineData("agent/01")]   // slash not allowed
    [InlineData("Ärger")]      // non-ASCII not allowed
    public void Validate_InvalidCharsInAgentId_ThrowsArgumentException(string agentId)
    {
        var descriptor = ValidDescriptor(agentId: agentId);
        var ex = Assert.Throws<ArgumentException>(() => descriptor.Validate());
        Assert.Contains("AgentId", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Validate() — Name constraints
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyName_ThrowsArgumentException()
    {
        var descriptor = ValidDescriptor(name: "");
        var ex = Assert.Throws<ArgumentException>(() => descriptor.Validate());
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Validate_WhitespaceName_ThrowsArgumentException()
    {
        var descriptor = ValidDescriptor(name: "   ");
        Assert.Throws<ArgumentException>(() => descriptor.Validate());
    }

    // -------------------------------------------------------------------------
    // Validate() — Capabilities constraints
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_CapabilitiesContainsEmptyString_ThrowsArgumentException()
    {
        var descriptor = new AgentDescriptor
        {
            AgentId = "agent-01",
            Name = "Test Agent",
            Status = AgentStatus.Unregistered,
            Capabilities = new[] { "ingest", "" },
            RegisteredAt = DateTime.UtcNow
        };
        Assert.Throws<ArgumentException>(() => descriptor.Validate());
    }

    [Fact]
    public void Validate_CapabilitiesContainsWhitespace_ThrowsArgumentException()
    {
        var descriptor = new AgentDescriptor
        {
            AgentId = "agent-01",
            Name = "Test Agent",
            Status = AgentStatus.Unregistered,
            Capabilities = new[] { "ingest", "   " },
            RegisteredAt = DateTime.UtcNow
        };
        Assert.Throws<ArgumentException>(() => descriptor.Validate());
    }

    // -------------------------------------------------------------------------
    // Construction and immutability
    // -------------------------------------------------------------------------

    [Fact]
    public void Construction_AllRequiredFieldsSet_PropertiesReturnCorrectValues()
    {
        var now = DateTime.UtcNow;
        var descriptor = new AgentDescriptor
        {
            AgentId = "my-agent",
            Name = "My Agent",
            Status = AgentStatus.Running,
            Capabilities = new[] { "query" },
            RegisteredAt = now,
            LastHealthCheckAt = now
        };

        Assert.Equal("my-agent", descriptor.AgentId);
        Assert.Equal("My Agent", descriptor.Name);
        Assert.Equal(AgentStatus.Running, descriptor.Status);
        Assert.Single(descriptor.Capabilities!);
        Assert.Equal("query", descriptor.Capabilities![0]);
        Assert.Equal(now, descriptor.RegisteredAt);
        Assert.Equal(now, descriptor.LastHealthCheckAt);
    }

    [Fact]
    public void Construction_WithExpression_CreatesNewRecordWithUpdatedField()
    {
        var original = ValidDescriptor();
        // Records are immutable; `with` produces a new instance
        var updated = original with { Status = AgentStatus.Running };

        Assert.Equal(AgentStatus.Unregistered, original.Status);
        Assert.Equal(AgentStatus.Running, updated.Status);
        Assert.NotSame(original, updated);
        // Other fields are unchanged
        Assert.Equal(original.AgentId, updated.AgentId);
        Assert.Equal(original.Name, updated.Name);
    }

    [Fact]
    public void Equality_TwoIdenticalRecords_AreEqual()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = new AgentDescriptor { AgentId = "x", Name = "X", Status = AgentStatus.Unregistered, RegisteredAt = now };
        var b = new AgentDescriptor { AgentId = "x", Name = "X", Status = AgentStatus.Unregistered, RegisteredAt = now };

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentAgentId_AreNotEqual()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = new AgentDescriptor { AgentId = "x", Name = "X", Status = AgentStatus.Unregistered, RegisteredAt = now };
        var b = new AgentDescriptor { AgentId = "y", Name = "X", Status = AgentStatus.Unregistered, RegisteredAt = now };

        Assert.NotEqual(a, b);
    }
}
