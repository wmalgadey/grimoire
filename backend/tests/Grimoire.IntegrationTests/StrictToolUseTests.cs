using System.Net;
using System.Text.Json;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.TestSupport;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #127 — the tool definitions are sent with <c>strict: true</c>, so the provider validates
/// <c>tool_use.input</c> against the schema before it reaches us. A mis-shaped input no
/// longer costs a turn against the turn cap, and a denial record no longer means two
/// different things.
/// <para>
/// The second half of this file is the more important one. Strict tool use is <em>not</em>
/// a guardrail and must never be read as one: it constrains the <em>shape</em> of a tool
/// call, while Principle V's deny-by-default boundary is about <em>authorization</em>. A
/// perfectly schema-valid write to a forbidden path is exactly the case where those two
/// come apart, so it is pinned here — if this file ever justifies relaxing
/// <c>GuardedToolExecutor</c>, these tests are the ones that say no.
/// </para>
/// </summary>
public class StrictToolUseTests
{
    // ── What goes on the wire ────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryOfferedTool_IsSentAsStrict_WithAClosedSchema()
    {
        // Strict mode requires both halves: `required` (which the schemas already had) and
        // `additionalProperties: false` (which they did not). Half of it is not strict.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        var tools = await SentToolsAsync(provider, ToolRegistry.Default.Tools);

        Assert.Equal(3, tools.Count);
        Assert.All(tools, tool =>
        {
            Assert.True(tool.GetProperty("strict").GetBoolean());
            var schema = tool.GetProperty("input_schema");
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            Assert.NotEmpty(schema.GetProperty("required").EnumerateArray());
        });
    }

    [Fact]
    public async Task TheToolNamesAndOrderOnTheWire_AreTheRegistrysOwn()
    {
        // The tool list the model sees is the registry's declaration (ADR-011 R3/R11) —
        // strict changes how an input is validated, never which tools are offered.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        var tools = await SentToolsAsync(provider, ToolRegistry.Default.Tools);

        Assert.Equal(
            ToolRegistry.Default.Tools.Select(t => t.Name),
            tools.Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task AReadOnlyRegistry_StillOffersNoWriteTool()
    {
        // An agent process configured with a narrower registry must not gain a tool from
        // this change.
        var readOnly = new ToolRegistry([ToolRegistry.ListFilesDefinition, ToolRegistry.ReadFileDefinition]);
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        var tools = await SentToolsAsync(provider, readOnly.Tools);

        Assert.DoesNotContain(ToolRegistry.WriteFile, tools.Select(t => t.GetProperty("name").GetString()));
    }

    // ── What strict deliberately does not do ─────────────────────────────────────────

    [Fact]
    public async Task ASchemaValidWriteToAForbiddenPath_IsStillDenied()
    {
        using var workspace = new PolicyWorkspace();

        // Shape-perfect: both required properties, correct types, nothing extra. Strict
        // mode would pass this input through untouched. The policy still refuses it.
        var result = await workspace.Executor.ExecuteAsync(
            ToolRegistry.WriteFile,
            JsonSerializer.Serialize(new { path = "outside/forbidden.md", content = "# Page\n" }),
            turn: 1,
            CancellationToken.None);

        Assert.True(result.IsError);
        var denial = Assert.Single(workspace.Executor.Denials);
        Assert.Equal(ToolRegistry.WriteFile, denial.Action);
        Assert.False(File.Exists(Path.Combine(workspace.Root, "outside", "forbidden.md")));
    }

    [Fact]
    public async Task ASchemaValidWriteInsideTheWriteScope_IsStillAllowed()
    {
        // The other side of the same contract: closing the schema must not have narrowed
        // what a legitimate write can do.
        using var workspace = new PolicyWorkspace();

        var result = await workspace.Executor.ExecuteAsync(
            ToolRegistry.WriteFile,
            JsonSerializer.Serialize(new { path = "wiki/allowed.md", content = "# Page\n" }),
            turn: 1,
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(workspace.Executor.Denials);
        Assert.True(File.Exists(Path.Combine(workspace.Root, "wiki", "allowed.md")));
    }

    [Fact]
    public async Task AToolTheRegistryDoesNotOffer_IsStillRejected()
    {
        // The provider validates the input of tools it was given. It says nothing about a
        // name that was never offered, and the registry lookup remains ours.
        using var workspace = new PolicyWorkspace();

        var result = await workspace.Executor.ExecuteAsync(
            "delete_file",
            JsonSerializer.Serialize(new { path = "wiki/allowed.md" }),
            turn: 1,
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    private static async Task<IReadOnlyList<JsonElement>> SentToolsAsync(
        FakeAnthropicEndpoint provider, IReadOnlyList<ToolDefinition> tools)
    {
        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var client = new AnthropicModelClient(
            logger: null!,
            modelEnvVar: scope.ModelEnvVar,
            baseUrlEnvVar: scope.BaseUrlEnvVar,
            maxOutputTokensEnvVar: $"GRIMOIRE_TEST_MAX_OUTPUT_UNSET_{Guid.NewGuid():N}");

        await client.NextTurnAsync(
            "You are a test agent.",
            [new ConversationMessage("user", "Do the task.")],
            tools,
            CancellationToken.None);

        using var document = JsonDocument.Parse(provider.Requests[0]);
        return [.. document.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.Clone())];
    }

    /// <summary>A real on-disk workspace with a write scope narrower than its read scope.</summary>
    private sealed class PolicyWorkspace : IDisposable
    {
        public PolicyWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"strict-tool-use-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "wiki"));
            Directory.CreateDirectory(Path.Combine(Root, "outside"));

            var policy = new SafetyPolicy(
                Root,
                readPrefixes: [Root + Path.DirectorySeparatorChar],
                writePrefixes: [Path.Combine(Root, "wiki") + Path.DirectorySeparatorChar]);
            Executor = new GuardedToolExecutor(policy, new WriteJournal(), Root);
        }

        public string Root { get; }

        public GuardedToolExecutor Executor { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }
}
