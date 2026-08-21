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
        var childEnv = Build(agent, BaseEnv((modelVar, InheritedModel)), model: SecretsFileModel);

        Assert.Equal(SecretsFileModel, childEnv[modelVar]);
        Assert.False(childEnv.ContainsKey(baseUrlVar));
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
