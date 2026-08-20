using System.Net;
using System.Text.Json;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.IntegrationTests.TestSupport;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #117 FR-001 and #122 — the two values the adapter used to decide for itself.
/// <para>
/// The model id had a <c>claude-opus-4-8</c> literal behind it, a different tier than the
/// one the deployment configures, applied silently and per-agent: since the three model
/// variables inherit from no other, setting only <c>GRIMOIRE_INGEST_MODEL</c> left Query
/// and Lint on a model nobody chose. The output ceiling was the literal <c>8096</c>,
/// enforced on every request for every agent, and reaching it truncates a turn mid-thought.
/// </para>
/// <para>
/// Both are asserted where they are observable — on the wire, against a real listener, by
/// what the adapter actually sends or refuses to send — rather than by reflecting over the
/// adapter's shape (Constitution III: a Feature-Scoped Invariant gets a behavioral test).
/// </para>
/// </summary>
public class ModelClientConfigurationTests
{
    // ── #117 FR-001: the model id comes from configuration, or the run fails ──────────

    [Fact]
    public void AnUnsetModelVariable_FailsClosed_NamingTheVariableToSet()
    {
        var unsetName = $"GRIMOIRE_TEST_MODEL_{Guid.NewGuid():N}";

        var exception = Assert.Throws<InvalidOperationException>(
            () => new AnthropicModelClient(logger: null!, modelEnvVar: unsetName));

        Assert.Contains(unsetName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankModelVariable_FailsClosedToo()
    {
        // An operator who commented the value out but left the key, or whose .env has a
        // trailing `=`, is in exactly the position the fallback used to hide.
        var name = $"GRIMOIRE_TEST_MODEL_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, "   ");
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => new AnthropicModelClient(logger: null!, modelEnvVar: name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task TheConfiguredModel_IsTheModelSentToTheProvider()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        using var scope = ModelClientEnvironmentScope.PointingAt(
            provider.BaseUrl, model: "claude-configured-by-the-operator");
        var client = ClientFor(scope);

        Assert.Equal("claude-configured-by-the-operator", client.ModelId);
        await NextTurnAsync(client);
        Assert.Equal("claude-configured-by-the-operator", RequestProperty(provider, "model").GetString());
    }

    // ── #122: the output ceiling is per-agent configuration, not a literal ────────────

    [Fact]
    public async Task WithNoCeilingConfigured_TheAdapterSendsItsDocumentedDefault()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        await NextTurnAsync(ClientFor(scope));

        Assert.Equal(
            AnthropicModelClient.DefaultMaxOutputTokens,
            RequestProperty(provider, "max_tokens").GetInt32());
    }

    [Fact]
    public async Task AConfiguredCeiling_IsTheCeilingSentToTheProvider()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var ceilingEnvVar = $"GRIMOIRE_TEST_MAX_OUTPUT_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(ceilingEnvVar, "32000");
        try
        {
            await NextTurnAsync(ClientFor(scope, ceilingEnvVar));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ceilingEnvVar, null);
        }

        Assert.Equal(32_000, RequestProperty(provider, "max_tokens").GetInt32());
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task AnUnusableCeiling_FallsBackToTheDefault_RatherThanFailingTheRun(string configured)
    {
        // Unlike the model id, a mistyped ceiling has a safe reading, and the run is still
        // one the operator wants to happen — so this degrades instead of failing closed.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
        var ceilingEnvVar = $"GRIMOIRE_TEST_MAX_OUTPUT_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(ceilingEnvVar, configured);
        try
        {
            await NextTurnAsync(ClientFor(scope, ceilingEnvVar));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ceilingEnvVar, null);
        }

        Assert.Equal(
            AnthropicModelClient.DefaultMaxOutputTokens,
            RequestProperty(provider, "max_tokens").GetInt32());
    }

    [Fact]
    public async Task EachAgentsCeiling_IsReadFromItsOwnVariable()
    {
        // ADR-004 scoping: what Query may emit for one answer and what Ingest needs for a
        // full wiki page are not the same number, and one agent's setting must not reach
        // another's client.
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.OK, FakeAnthropicEndpoint.MessageBody("end_turn", text: "Done."));

        var suffix = Guid.NewGuid().ToString("N");
        var ingestCeiling = $"GRIMOIRE_TEST_INGEST_MAX_{suffix}";
        var queryCeiling = $"GRIMOIRE_TEST_QUERY_MAX_{suffix}";
        Environment.SetEnvironmentVariable(ingestCeiling, "20000");
        Environment.SetEnvironmentVariable(queryCeiling, "4000");
        try
        {
            using var scope = ModelClientEnvironmentScope.PointingAt(provider.BaseUrl);
            await NextTurnAsync(ClientFor(scope, ingestCeiling));
            await NextTurnAsync(ClientFor(scope, queryCeiling));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ingestCeiling, null);
            Environment.SetEnvironmentVariable(queryCeiling, null);
        }

        Assert.Equal(20_000, RequestProperty(provider, "max_tokens", requestIndex: 0).GetInt32());
        Assert.Equal(4_000, RequestProperty(provider, "max_tokens", requestIndex: 1).GetInt32());
    }

    private static AnthropicModelClient ClientFor(
        ModelClientEnvironmentScope scope, string? maxOutputTokensEnvVar = null)
        => new(
            logger: null!,
            modelEnvVar: scope.ModelEnvVar,
            baseUrlEnvVar: scope.BaseUrlEnvVar,
            maxOutputTokensEnvVar: maxOutputTokensEnvVar
                ?? $"GRIMOIRE_TEST_MAX_OUTPUT_UNSET_{Guid.NewGuid():N}");

    private static Task<ModelTurn> NextTurnAsync(AnthropicModelClient client)
        => client.NextTurnAsync(
            "You are a test agent.",
            [new ConversationMessage("user", "Do the task.")],
            ToolRegistry.Default.Tools,
            CancellationToken.None);

    private static JsonElement RequestProperty(
        FakeAnthropicEndpoint provider, string property, int requestIndex = 0)
    {
        using var document = JsonDocument.Parse(provider.Requests[requestIndex]);
        return document.RootElement.GetProperty(property).Clone();
    }
}
