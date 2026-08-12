using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Replay;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T044 (ADR-022, SC-010) — replay eval runs are provably independent of Hub
/// configuration and of any hub-facing agent build: setting every
/// <c>Grimoire__Paths__*</c> environment variable the Hub's
/// <c>GrimoirePathResolver</c> reads to a nonexistent junk path, and removing the
/// runtime agent directory (<c>.grimoire/agents</c>, populated by
/// <c>PublishAgentRuntime</c>) entirely, yields identical sample counts and success rate
/// to a normal run — because <see cref="AgentProcessInvoker.ForRepo"/> resolves the agent
/// executable straight from the agent project's own build output
/// (<c>backend/src/Grimoire.IngestAgent/bin/...</c>), never through the Hub-configured
/// agent directory.
/// </summary>
[Trait("Tier", "SlowEval")]
[Collection("EvalRunnerEnvMutatingTests")]
public class EvalIndependenceFromHubConfigurationTests
{
    private static readonly string[] GrimoirePathsEnvVars =
    [
        "Grimoire__Paths__Data__Dir",
        "Grimoire__Paths__Wiki__Dir",
        "Grimoire__Paths__Agent__Dir",
        "Grimoire__Paths__Memory__Dir",
        "Grimoire__Paths__Data__RawDir",
        "Grimoire__Paths__Data__StateDb",
        "Grimoire__Paths__Data__WriteLocksDir",
        "Grimoire__Paths__Memory__TasksDir",
        "Grimoire__Paths__Memory__ConversationsDir",
        "Grimoire__Paths__Memory__FindingsDir",
        "Grimoire__Paths__Memory__RemediationTasksDir",
        "Grimoire__Paths__SecretsFile",
    ];

    [Fact]
    public async Task JunkHubPathEnvVarsAndNoAgentRuntimeDirectory_YieldIdenticalResults_ToANormalRun()
    {
        var paths = EvalPaths.Discover();
        var scenario = IngestScenarioDefinitions.UpdateOverDuplicate;

        var baseline = await RunScenarioAsync(paths, scenario);
        Assert.Equal(TrustStatus.Trusted, baseline.TrustStatus);

        var agentRuntimeDir = Path.Combine(paths.RepoRoot, ".grimoire", "agents");
        var movedAsideDir = agentRuntimeDir + "-t044-moved-aside";
        var agentRuntimeExisted = Directory.Exists(agentRuntimeDir);
        if (agentRuntimeExisted)
        {
            Directory.Move(agentRuntimeDir, movedAsideDir);
        }

        var savedEnv = GrimoirePathsEnvVars.ToDictionary(k => k, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var key in GrimoirePathsEnvVars)
            {
                Environment.SetEnvironmentVariable(key, $"/does/not/exist/t044-junk-{key}");
            }

            Assert.False(Directory.Exists(agentRuntimeDir), "Expected the runtime agent directory to be absent for this assertion.");

            var withJunkConfig = await RunScenarioAsync(paths, scenario);

            Assert.Equal(TrustStatus.Trusted, withJunkConfig.TrustStatus);
            Assert.Equal(baseline.Samples.Count, withJunkConfig.Samples.Count);
            Assert.Equal(baseline.SuccessRate, withJunkConfig.SuccessRate);
            Assert.Equal(baseline.ThresholdMet, withJunkConfig.ThresholdMet);
        }
        finally
        {
            foreach (var (key, value) in savedEnv)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
            if (agentRuntimeExisted)
            {
                Directory.Move(movedAsideDir, agentRuntimeDir);
            }
        }
    }

    private static async Task<ScenarioReplayResult> RunScenarioAsync(EvalPaths paths, ScenarioDefinition scenario)
    {
        var store = new RecordingStore(paths.RecordingsRoot);
        var pipeline = new ReplayPipeline(store, paths, AgentProcessInvoker.ForRepo(paths), NullLogger.Instance);
        return await pipeline.RunScenarioAsync(scenario, CancellationToken.None);
    }
}
