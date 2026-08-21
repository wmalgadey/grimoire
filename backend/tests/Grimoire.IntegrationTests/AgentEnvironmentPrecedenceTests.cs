using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #61 — one precedence rule for every agent's <c>GRIMOIRE_*</c> variables: the secrets
/// file wins, the Hub's own process environment is the fallback, and nothing is dropped
/// without saying so. Ingest already worked this way; Query and Lint discarded the
/// inherited value unconditionally, so an operator setting <c>GRIMOIRE_*_BASE_URL</c> in a
/// shell or a launch profile got it applied for one agent and ignored for the other two.
///
/// <para>
/// Each case is asserted against all three env builders rather than Query's alone: the
/// defect was the three of them disagreeing, so a test that pins only the one that was
/// wrong would let them drift apart again.
/// </para>
/// </summary>
public class AgentEnvironmentPrecedenceTests
{
    private const string InheritedModel = "model-from-the-hub-environment";
    private const string SecretsFileModel = "model-from-the-secrets-file";

    private static Dictionary<string, string> BaseEnv(params (string Key, string Value)[] entries)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PATH"] = "/usr/bin" };
        foreach (var (key, value) in entries)
        {
            env[key] = value;
        }

        return env;
    }

    public static TheoryData<string, string, string> Agents => new()
    {
        { "ingest", "GRIMOIRE_INGEST_MODEL", "GRIMOIRE_INGEST_BASE_URL" },
        { "query", "GRIMOIRE_QUERY_MODEL", "GRIMOIRE_QUERY_BASE_URL" },
        { "lint", "GRIMOIRE_LINT_MODEL", "GRIMOIRE_LINT_BASE_URL" },
    };

    /// <summary>
    /// Every variable the precedence rule now governs, not just the two the first version of
    /// this file happened to name. The rule changed the caps too, so a regression that
    /// dropped or reversed one of them stayed green while the model/base-url matrix passed.
    /// Each row is (agent, variable, the builder argument that carries it).
    /// </summary>
    public static TheoryData<string, string> EveryGovernedVariable => new()
    {
        { "ingest", "GRIMOIRE_INGEST_MODEL" },
        { "ingest", "GRIMOIRE_INGEST_BASE_URL" },
        { "ingest", "GRIMOIRE_INGEST_MAX_OUTPUT_TOKENS" },
        { "ingest", "GRIMOIRE_INGEST_SPEND_CAP" },
        { "query", "GRIMOIRE_QUERY_MODEL" },
        { "query", "GRIMOIRE_QUERY_BASE_URL" },
        { "query", "GRIMOIRE_QUERY_MAX_OUTPUT_TOKENS" },
        { "lint", "GRIMOIRE_LINT_MODEL" },
        { "lint", "GRIMOIRE_LINT_BASE_URL" },
        { "lint", "GRIMOIRE_LINT_MAX_OUTPUT_TOKENS" },
    };

    /// <summary>
    /// Builds the child env with <paramref name="variable"/> supplied by the secrets file, or
    /// with the secrets file silent about it when <paramref name="value"/> is null — which is
    /// the whole point of two of the three cases below, and what the first version of this
    /// helper got wrong by routing the value unconditionally.
    /// </summary>
    private static Dictionary<string, string> BuildWithSecretsValue(
        string agent, string variable, string? value, IDictionary<string, string> baseEnv, ILogger? logger = null)
        => agent switch
        {
            "ingest" => AgentProcessHost.BuildChildEnvironment(
                baseEnv,
                authToken: null,
                ingestBaseUrl: value is not null && variable.EndsWith("BASE_URL", StringComparison.Ordinal) ? value : null,
                ingestModel: value is not null && variable.EndsWith("MODEL", StringComparison.Ordinal) ? value : null,
                ingestTokenCap: null,
                ingestMaxOutputTokens: value is not null && variable.EndsWith("MAX_OUTPUT_TOKENS", StringComparison.Ordinal) ? value : null,
                logger: logger,
                ingestSpendCap: value is not null && variable.EndsWith("SPEND_CAP", StringComparison.Ordinal) ? value : null),
            "query" => AgentProcessHost.BuildQueryChildEnvironment(
                baseEnv,
                authToken: null,
                queryBaseUrl: value is not null && variable.EndsWith("BASE_URL", StringComparison.Ordinal) ? value : null,
                queryModel: value is not null && variable.EndsWith("MODEL", StringComparison.Ordinal) ? value : null,
                queryMaxOutputTokens: value is not null && variable.EndsWith("MAX_OUTPUT_TOKENS", StringComparison.Ordinal) ? value : null,
                logger: logger),
            "lint" => AgentProcessHost.BuildLintChildEnvironment(
                baseEnv,
                authToken: null,
                lintBaseUrl: value is not null && variable.EndsWith("BASE_URL", StringComparison.Ordinal) ? value : null,
                lintModel: value is not null && variable.EndsWith("MODEL", StringComparison.Ordinal) ? value : null,
                lintMaxOutputTokens: value is not null && variable.EndsWith("MAX_OUTPUT_TOKENS", StringComparison.Ordinal) ? value : null,
                logger: logger),
            _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "unknown agent"),
        };

    [Theory]
    [MemberData(nameof(EveryGovernedVariable))]
    public void EveryGovernedVariable_InheritsWhenTheSecretsFileIsSilent(string agent, string variable)
    {
        var childEnv = BuildWithSecretsValue(agent, variable, value: null, BaseEnv((variable, "inherited-value")));

        Assert.Equal("inherited-value", childEnv[variable]);
    }

    [Theory]
    [MemberData(nameof(EveryGovernedVariable))]
    public void EveryGovernedVariable_LetsTheSecretsFileWin(string agent, string variable)
    {
        var childEnv = BuildWithSecretsValue(
            agent, variable, value: "secrets-file-value", BaseEnv((variable, "inherited-value")));

        Assert.Equal("secrets-file-value", childEnv[variable]);
    }

    [Theory]
    [MemberData(nameof(EveryGovernedVariable))]
    public void EveryGovernedVariable_IsAbsentWhenNeitherSourceSetsIt(string agent, string variable)
    {
        var childEnv = BuildWithSecretsValue(agent, variable, value: null, BaseEnv());

        Assert.False(childEnv.ContainsKey(variable), $"{variable} must not appear when neither source set it.");
    }

    private static Dictionary<string, string> Build(
        string agent,
        IDictionary<string, string> baseEnv,
        string? model = null,
        string? baseUrl = null,
        ILogger? logger = null)
        => agent switch
        {
            "ingest" => AgentProcessHost.BuildChildEnvironment(
                baseEnv, authToken: null, ingestBaseUrl: baseUrl, ingestModel: model, logger: logger),
            "query" => AgentProcessHost.BuildQueryChildEnvironment(
                baseEnv, authToken: null, queryBaseUrl: baseUrl, queryModel: model, logger: logger),
            "lint" => AgentProcessHost.BuildLintChildEnvironment(
                baseEnv, authToken: null, lintBaseUrl: baseUrl, lintModel: model, logger: logger),
            _ => throw new ArgumentOutOfRangeException(nameof(agent), agent, "unknown agent"),
        };

    [Theory]
    [MemberData(nameof(Agents))]
    public void AValueOnlyTheHubEnvironmentCarries_ReachesTheAgent(string agent, string modelVar, string baseUrlVar)
    {
        var childEnv = Build(agent, BaseEnv((modelVar, InheritedModel), (baseUrlVar, "http://localhost:4000")));

        Assert.Equal(InheritedModel, childEnv[modelVar]);
        Assert.Equal("http://localhost:4000", childEnv[baseUrlVar]);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void TheSecretsFileWins_OverAValueTheHubEnvironmentAlsoCarries(string agent, string modelVar, string baseUrlVar)
    {
        // Both variables are inherited *and* both are set by the secrets file, so each one
        // exercises the winner branch. Supplying only the model left the base-url assertion
        // proving nothing but absence: a regression that reversed precedence for base URL
        // alone would still have passed.
        var childEnv = Build(
            agent,
            BaseEnv((modelVar, InheritedModel), (baseUrlVar, "http://inherited.invalid")),
            model: SecretsFileModel,
            baseUrl: "http://from-the-secrets-file.invalid");

        Assert.Equal(SecretsFileModel, childEnv[modelVar]);
        Assert.Equal("http://from-the-secrets-file.invalid", childEnv[baseUrlVar]);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void AVariableNeitherSourceSets_IsAbsentFromTheChildEnvironment(string agent, string modelVar, string baseUrlVar)
    {
        var childEnv = Build(agent, BaseEnv());

        Assert.False(childEnv.ContainsKey(modelVar));
        Assert.False(childEnv.ContainsKey(baseUrlVar));
        Assert.Equal("/usr/bin", childEnv["PATH"]);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public void TheAppliedValueIsLogged_WithTheSourceItCameFrom(string agent, string modelVar, string baseUrlVar)
    {
        var fromFile = new CaptureLogger<AgentEnvironmentPrecedenceTests>();
        Build(agent, BaseEnv(), model: SecretsFileModel, logger: fromFile);

        var applied = Assert.Single(fromFile.Entries.Where(e =>
            e.EventName == "agent.env.override_applied" && e.Fields["variable"]?.ToString() == modelVar));
        Assert.Equal(LogLevel.Information, applied.Level);
        Assert.Equal(agent, applied.Fields["agent"]?.ToString());
        Assert.Equal("secrets_file", applied.Fields["source"]?.ToString());
        Assert.Equal(SecretsFileModel, applied.Fields["value"]?.ToString());

        var fromEnvironment = new CaptureLogger<AgentEnvironmentPrecedenceTests>();
        Build(agent, BaseEnv((baseUrlVar, "http://localhost:4000")), logger: fromEnvironment);

        var inherited = Assert.Single(fromEnvironment.Entries.Where(e =>
            e.EventName == "agent.env.override_applied" && e.Fields["variable"]?.ToString() == baseUrlVar));
        Assert.Equal("process_env", inherited.Fields["source"]?.ToString());
        Assert.Equal("http://localhost:4000", inherited.Fields["value"]?.ToString());
    }

    /// <summary>
    /// The half of #61 that is not about which value wins: an override the operator set in
    /// the Hub's environment and that lost to the secrets file must be reported, so the
    /// answer to "why is this agent not using the model I set?" is in the log rather than
    /// in the source.
    /// </summary>
    [Theory]
    [MemberData(nameof(Agents))]
    public void AnInheritedValueThatLosesToTheSecretsFile_IsReported(string agent, string modelVar, string baseUrlVar)
    {
        var logger = new CaptureLogger<AgentEnvironmentPrecedenceTests>();
        Build(agent, BaseEnv((modelVar, InheritedModel)), model: SecretsFileModel, logger: logger);

        var superseded = Assert.Single(logger.Entries.Where(e => e.EventName == "agent.env.override_superseded"));
        Assert.Equal(LogLevel.Information, superseded.Level);
        Assert.Equal(agent, superseded.Fields["agent"]?.ToString());
        Assert.Equal(modelVar, superseded.Fields["variable"]?.ToString());
        Assert.Equal("process_env", superseded.Fields["superseded_source"]?.ToString());
        Assert.Equal("secrets_file", superseded.Fields["winning_source"]?.ToString());

        var quiet = new CaptureLogger<AgentEnvironmentPrecedenceTests>();
        Build(agent, BaseEnv((baseUrlVar, "http://localhost:4000")), model: SecretsFileModel, logger: quiet);

        Assert.Empty(quiet.Entries.Where(e =>
            e.EventName == "agent.env.override_superseded" && e.Fields["variable"]?.ToString() == baseUrlVar));
    }

    // ── The spend cap, which answers to two names ────────────────────────────────────

    /// <summary>
    /// The agent reads <c>GRIMOIRE_INGEST_SPEND_CAP</c> before the legacy
    /// <c>GRIMOIRE_INGEST_TOKEN_CAP</c> (<c>Grimoire.IngestAgent/Program.cs</c>). Handling
    /// only the alias in the Hub left the canonical key unscrubbed, so it travelled through
    /// from the Hub's own environment and beat the secrets file — the precedence rule broken
    /// for the one variable with two names.
    /// </summary>
    [Fact]
    public void TheSecretsFileCap_Wins_OverACanonicalSpendCapInTheHubEnvironment()
    {
        var childEnv = AgentProcessHost.BuildChildEnvironment(
            BaseEnv(("GRIMOIRE_INGEST_SPEND_CAP", "200")),
            authToken: null,
            ingestTokenCap: "100");

        Assert.Equal("100", childEnv["GRIMOIRE_INGEST_SPEND_CAP"]);
        Assert.False(
            childEnv.ContainsKey("GRIMOIRE_INGEST_TOKEN_CAP"),
            "The legacy alias must not travel alongside the canonical name — the agent would see both.");
    }

    /// <summary>A secrets file setting the canonical name was previously never read at all.</summary>
    [Fact]
    public void TheSecretsFileCanSetTheCanonicalCapName()
    {
        var childEnv = AgentProcessHost.BuildChildEnvironment(
            BaseEnv(), authToken: null, ingestSpendCap: "250000");

        Assert.Equal("250000", childEnv["GRIMOIRE_INGEST_SPEND_CAP"]);
    }

    /// <summary>Where the secrets file sets both, the canonical name wins.</summary>
    [Fact]
    public void TheCanonicalCapName_WinsOverTheLegacyAlias_WithinTheSecretsFile()
    {
        var childEnv = AgentProcessHost.BuildChildEnvironment(
            BaseEnv(), authToken: null, ingestTokenCap: "100", ingestSpendCap: "250000");

        Assert.Equal("250000", childEnv["GRIMOIRE_INGEST_SPEND_CAP"]);
    }

    /// <summary>The legacy alias still works as an input, and is normalised to the canonical name.</summary>
    [Fact]
    public void TheLegacyAlias_IsStillAccepted_AndForwardedUnderTheCanonicalName()
    {
        var inherited = AgentProcessHost.BuildChildEnvironment(
            BaseEnv(("GRIMOIRE_INGEST_TOKEN_CAP", "777")), authToken: null);

        Assert.Equal("777", inherited["GRIMOIRE_INGEST_SPEND_CAP"]);
        Assert.False(inherited.ContainsKey("GRIMOIRE_INGEST_TOKEN_CAP"));
    }

    /// <summary>
    /// ADR-004's scrubbing is unchanged by the precedence fix: the credential is never
    /// inherited, for any agent, whatever the Hub's own environment carries.
    /// </summary>
    [Theory]
    [MemberData(nameof(Agents))]
    public void TheCredentialIsStillNeverInherited(string agent, string modelVar, string baseUrlVar)
    {
        _ = modelVar;
        _ = baseUrlVar;
        var childEnv = Build(agent, BaseEnv(
            ("ANTHROPIC_AUTH_TOKEN", "sk-ant-inherited-from-parent"),
            ("ANTHROPIC_API_KEY", "sk-legacy-inherited-from-parent")));

        Assert.False(childEnv.ContainsKey("ANTHROPIC_AUTH_TOKEN"));
        Assert.False(childEnv.ContainsKey("ANTHROPIC_API_KEY"));
    }
}
