using Grimoire.EvalRunner.Providers;

namespace Grimoire.AgentEvals;

/// <summary>
/// Hermetic tests for the eval provider gate (contracts/eval-provider-env-vars.md,
/// data-model.md#EvalGateOutcome). No live provider call, network access, or GRIMOIRE_EVAL
/// gating required — env vars are injected via the internal overload rather than read from
/// the real process environment.
/// </summary>
[Trait("Tier", "Fast")]
public class EvalProviderResolverTests
{
    [Fact]
    public void Resolve_NeitherConfigured_ReturnsSkippedNamingBothOptions()
    {
        var outcome = EvalProviderResolver.Resolve(Env());

        Assert.Equal(EvalGateStatus.Skipped, outcome.Status);
        Assert.Equal(ProviderKind.None, outcome.Configuration.Kind);
        Assert.NotNull(outcome.Reason);
        Assert.Contains("ANTHROPIC_AUTH_TOKEN", outcome.Reason, StringComparison.Ordinal);
        Assert.Contains("GRIMOIRE_EVAL_PROVIDER_BASE_URL", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AffordableOnlyComplete_ReturnsEnabledAffordable()
    {
        var outcome = EvalProviderResolver.Resolve(Env(
            ("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:4000"),
            ("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model"),
            ("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key")));

        Assert.Equal(EvalGateStatus.Enabled, outcome.Status);
        Assert.Equal(ProviderKind.Affordable, outcome.Configuration.Kind);
        Assert.Equal("http://localhost:4000", outcome.Configuration.BaseUrl);
        Assert.Equal("nvidia-model", outcome.Configuration.Model);
        Assert.True(outcome.Configuration.HasCredential);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void Resolve_AnthropicOnlyConfigured_ReturnsEnabledAnthropic()
    {
        var outcome = EvalProviderResolver.Resolve(Env(("ANTHROPIC_AUTH_TOKEN", "sk-ant-fake")));

        Assert.Equal(EvalGateStatus.Enabled, outcome.Status);
        Assert.Equal(ProviderKind.Anthropic, outcome.Configuration.Kind);
        Assert.Null(outcome.Configuration.BaseUrl);
        Assert.True(outcome.Configuration.HasCredential);
        Assert.Null(outcome.Reason);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void Resolve_PartialAffordableConfig_NotCountedAsPresent(bool setBaseUrl, bool setModel, bool setKey)
    {
        var entries = new List<(string, string)>();
        if (setBaseUrl)
        {
            entries.Add(("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:4000"));
        }

        if (setModel)
        {
            entries.Add(("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model"));
        }

        if (setKey)
        {
            entries.Add(("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key"));
        }

        var outcome = EvalProviderResolver.Resolve(Env(entries.ToArray()));

        Assert.Equal(EvalGateStatus.Skipped, outcome.Status);
        Assert.Equal(ProviderKind.None, outcome.Configuration.Kind);
    }

    [Fact]
    public void Resolve_AnthropicWithPartialAffordableConfig_IsNotAConflict_ReturnsEnabledAnthropic()
    {
        var outcome = EvalProviderResolver.Resolve(Env(
            ("ANTHROPIC_AUTH_TOKEN", "sk-ant-fake"),
            ("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key")));

        Assert.Equal(EvalGateStatus.Enabled, outcome.Status);
        Assert.Equal(ProviderKind.Anthropic, outcome.Configuration.Kind);
    }

    [Fact]
    public void Resolve_AnthropicAndCompleteAffordableBothConfigured_ReturnsConfigurationErrorNamingConflict()
    {
        var outcome = EvalProviderResolver.Resolve(Env(
            ("ANTHROPIC_AUTH_TOKEN", "sk-ant-fake"),
            ("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:4000"),
            ("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model"),
            ("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key")));

        Assert.Equal(EvalGateStatus.ConfigurationError, outcome.Status);
        Assert.Equal(ProviderKind.None, outcome.Configuration.Kind);
        Assert.NotNull(outcome.Reason);
        Assert.Contains("ANTHROPIC_AUTH_TOKEN", outcome.Reason, StringComparison.Ordinal);
        Assert.Contains("GRIMOIRE_EVAL_PROVIDER", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AffordableModelIsPopulated_ForSC002Transparency()
    {
        var outcome = EvalProviderResolver.Resolve(Env(
            ("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:4000"),
            ("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model"),
            ("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key")));

        Assert.Equal("nvidia-model", outcome.Configuration.Model);
    }

    private static Func<string, string?> Env(params (string Key, string Value)[] entries)
    {
        var map = entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        return key => map.TryGetValue(key, out var value) ? value : null;
    }
}

// T016 (US1)'s EvalCredentialRedactionTests.SanitizeErrorText_RedactsConfiguredAffordableProviderApiKey
// was removed (constitution v1.9.0 "Test what we own"): it was a strict subset — one
// credential source — of CaptureHygieneTests.SanitizeErrorText_RedactsBothCredentialSources,
// which covers both sources and already carries the env-mutation collection cost.
