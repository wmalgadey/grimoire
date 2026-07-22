namespace Grimoire.AgentEvals;

/// <summary>
/// Hermetic tests for the eval provider gate (contracts/eval-provider-env-vars.md,
/// data-model.md#EvalGateOutcome). No live provider call, network access, or GRIMOIRE_EVAL
/// gating required — env vars are injected via the internal overload rather than read from
/// the real process environment.
/// </summary>
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

/// <summary>
/// T016 (US1) — the harness half of FR-008: the configured affordable-provider credential
/// must never appear in a failure's <c>FailureReason</c>/<c>Narrative</c>. Mutates the real
/// process environment (the sanitizer reads it directly, mirroring Program.cs), so this
/// runs in the sequential <c>EvalProviderEnvironment</c> collection.
/// </summary>
[Collection("EvalProviderEnvironment")]
public class EvalCredentialRedactionTests
{
    [Fact]
    public void SanitizeErrorText_RedactsConfiguredAffordableProviderApiKey()
    {
        const string fakeKey = "nvapi-super-secret-eval-key-0123456789";
        var originalKey = Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY");
        var originalToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", fakeKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);

            var rejectedAuthMessage = $"401 Unauthorized: invalid credential '{fakeKey}' for request to affordable provider";

            var sanitized = AgentEvalRunner.SanitizeErrorText(rejectedAuthMessage);

            Assert.DoesNotContain(fakeKey, sanitized, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", sanitized, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", originalKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", originalToken);
        }
    }
}
