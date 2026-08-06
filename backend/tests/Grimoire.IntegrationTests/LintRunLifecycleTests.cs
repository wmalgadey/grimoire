using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T015/T016 (013-lint-agent, US1, SC-001) — triggering a Lint Run through
/// <see cref="LintRunCoordinator"/> against a <see cref="FakeAgentProcessLauncher"/>
/// produces exactly one Findings Report file at
/// <c>&lt;base&gt;/data/findings/&lt;runId&gt;.md</c> with the documented frontmatter/
/// bookkeeping shape (contracts/findings-report-format.md); a missing/failed
/// instruction load fails the run before any narrative with instruction identity
/// omitted; an honest "no findings" narrative for a healthy wiki round-trips verbatim.
/// Hermetic — no live LLM call, no real agent process (mirrors
/// QueryInstructionLoadTests/QueryTurnSubmissionApiTests' coordinator-level idiom).
/// </summary>
public class LintRunLifecycleTests
{
    [Fact]
    public async Task TriggerAsync_CompletedRun_WritesExactlyOneFindingsReport_WithInstructionIdentityAndHash()
    {
        using var harness = LintCoordinatorHarness.Create();
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["systemPromptSha256"] = "abc123deadbeef",
            ["policyPath"] = "agents/lint/policy.json",
            ["policyVersion"] = 1,
            ["policySha256"] = "policyhash",
            ["model"] = "claude-test",
            ["turnsUsed"] = 3,
        };

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        var run = await harness.WaitForTerminalAsync(runId);
        Assert.Equal(LintRunStatus.Completed, run.Status);

        var findingsFiles = Directory.GetFiles(harness.Paths.FindingsDir, "*.md");
        var reportPath = Assert.Single(findingsFiles);
        Assert.Equal(harness.Paths.FindingsReportPathFor(runId), reportPath);

        var content = await File.ReadAllTextAsync(reportPath);
        Assert.Contains($"run_id: {runId}", content, StringComparison.Ordinal);
        Assert.Contains("record_format: grimoire-findings/1", content, StringComparison.Ordinal);
        Assert.Contains("outcome_state: completed", content, StringComparison.Ordinal);
        Assert.Contains("sha256: \"abc123deadbeef\"", content, StringComparison.Ordinal);
        Assert.Contains("path: \"agents/lint/system-prompt.md\"", content, StringComparison.Ordinal);
        Assert.Contains("partial: false", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerAsync_InstructionLoadFailure_FailsBeforeAnyNarrative_WithInstructionIdentityOmitted()
    {
        var reason = "Instruction document not found at 'agents/lint/system-prompt.md'. Cannot start a run without agent operating rules.";
        using var harness = LintCoordinatorHarness.Create(
            new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: reason, autoPlay: true));

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        var run = await harness.WaitForTerminalAsync(runId);
        Assert.Equal(LintRunStatus.Failed, run.Status);
        Assert.Equal(reason, run.FailureReason);

        var content = await File.ReadAllTextAsync(harness.Paths.FindingsReportPathFor(runId));
        Assert.Contains("outcome_state: failed", content, StringComparison.Ordinal);
        // JSON-escaped (mirrors ConversationRecordFormat's escaping rule, e.g. the
        // apostrophes in the reason text become ') — assert the unescaped prefix
        // rather than the literal reason string.
        Assert.Contains("failure_reason: \"Instruction document not found at", content, StringComparison.Ordinal);
        Assert.Contains("partial: true", content, StringComparison.Ordinal);
        // Instruction identity omitted: the load never succeeded (SC-001).
        Assert.Contains("path: null", content, StringComparison.Ordinal);
        Assert.Contains("sha256: null", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerAsync_HealthyWikiNarrative_RoundTripsVerbatim_WithExplicitNoFindingsStatements()
    {
        const string healthyNarrative =
            """
            ## Content Quality

            No content-quality findings.

            ## Metadata Hygiene

            No metadata-hygiene findings.

            ## Structure

            No structure findings.
            """;

        using var harness = LintCoordinatorHarness.Create();
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["summary"] = healthyNarrative,
        };

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        await harness.WaitForTerminalAsync(runId);

        var content = await File.ReadAllTextAsync(harness.Paths.FindingsReportPathFor(runId));
        Assert.Contains("No content-quality findings.", content, StringComparison.Ordinal);
        Assert.Contains("No metadata-hygiene findings.", content, StringComparison.Ordinal);
        Assert.Contains("No structure findings.", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerAsync_WhileARunIsActive_IsRejectedImmediately_WithoutQueuing()
    {
        using var harness = LintCoordinatorHarness.Create(
            new FakeAgentProcessLauncher(simulatedRunDuration: TimeSpan.FromSeconds(5)));

        var first = await harness.Coordinator.TriggerAsync();
        Assert.IsType<LintSubmissionResult.Accepted>(first);

        var second = await harness.Coordinator.TriggerAsync();
        Assert.IsType<LintSubmissionResult.Busy>(second);

        Assert.Single(harness.Launcher.LintRequests);
    }

    // ── T034 (013-lint-agent, US2, FR-008/spec.md acceptance scenario 4) ───────────────
    //
    // "Review candidate" classification is wiki-content judgment (Constitution Principle
    // V) and lives entirely in data/agents/lint/system-prompt.md's "Review candidates"
    // rule — it cannot be re-implemented as a deterministic backend check. What the
    // harness DOES own, and what is hermetically verifiable here, is two things: (1) the
    // agent's own review-candidate narrative round-trips into the Findings Report exactly
    // as written (mirrors T016's honest-empty-result guarantee, applied to a non-empty
    // Metadata Hygiene sub-section instead), and (2) the effective Review Window value
    // (T036, Grimoire:LintReviewWindowDays) is correctly threaded into the spawned run's
    // request — default 90, or the configured override — regardless of what the agent
    // then does with it.

    [Fact]
    public async Task TriggerAsync_NarrativeWithReviewCandidateSubSection_RoundTripsVerbatim()
    {
        const string narrativeWithReviewCandidate =
            """
            ## Content Quality

            No content-quality findings.

            ## Metadata Hygiene

            ### Review candidates

            [[stale-topic]] is `low`-confidence and was last reviewed 2025-01-05, more than
            90 days ago — due for a fresh look.

            ## Structure

            No structure findings.
            """;

        using var harness = LintCoordinatorHarness.Create();
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["summary"] = narrativeWithReviewCandidate,
        };

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        await harness.WaitForTerminalAsync(runId);

        var content = await File.ReadAllTextAsync(harness.Paths.FindingsReportPathFor(runId));
        Assert.Contains("### Review candidates", content, StringComparison.Ordinal);
        Assert.Contains("[[stale-topic]] is `low`-confidence", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerAsync_NarrativeWithNoReviewCandidates_IsNotFabricated()
    {
        // FR-006: an honest "nothing due" sub-section is a valid, expected outcome — the
        // Hub never invents or suppresses a review candidate the agent didn't report.
        const string narrativeWithinWindow =
            """
            ## Content Quality

            No content-quality findings.

            ## Metadata Hygiene

            ### Review candidates

            No review candidates — every low-confidence page was reviewed within the window.

            ## Structure

            No structure findings.
            """;

        using var harness = LintCoordinatorHarness.Create();
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["summary"] = narrativeWithinWindow,
        };

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        await harness.WaitForTerminalAsync(runId);

        var content = await File.ReadAllTextAsync(harness.Paths.FindingsReportPathFor(runId));
        Assert.Contains("No review candidates", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TriggerAsync_NoReviewWindowConfigured_ThreadsTheDefaultNinetyDays_IntoTheAgentRequest()
    {
        using var harness = LintCoordinatorHarness.Create();

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        await harness.WaitForTerminalAsync(accepted.Run.RunId);

        var request = Assert.Single(harness.Launcher.LintRequests);
        Assert.Equal(90, request.ReviewWindowDays);
    }

    [Fact]
    public async Task TriggerAsync_ReviewWindowConfigured_ThreadsTheConfiguredValue_IntoTheAgentRequest()
    {
        using var harness = LintCoordinatorHarness.Create(
            reviewWindowOptions: new LintReviewWindowOptions { LintReviewWindowDays = 30 });

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
        await harness.WaitForTerminalAsync(accepted.Run.RunId);

        var request = Assert.Single(harness.Launcher.LintRequests);
        Assert.Equal(30, request.ReviewWindowDays);
    }
}

/// <summary>
/// Hermetic coordinator + temp findings-dir harness, mirroring
/// QueryTurnSubmissionApiTests.BuildResolvedPaths/BuildHostAsync — top-level (not nested)
/// so LintTraceTests.cs/other Lint test files can reuse it, same convention as
/// QueryTurnSubmissionApiTests' internal static helpers.
/// </summary>
internal sealed class LintCoordinatorHarness : IDisposable
{
    private readonly string _root;

    private LintCoordinatorHarness(string root, ResolvedGrimoirePaths paths, FakeAgentProcessLauncher launcher, LintRunCoordinator coordinator)
    {
        _root = root;
        Paths = paths;
        Launcher = launcher;
        Coordinator = coordinator;
    }

    public ResolvedGrimoirePaths Paths { get; }
    public FakeAgentProcessLauncher Launcher { get; }
    public LintRunCoordinator Coordinator { get; }

    public static LintCoordinatorHarness Create(
        FakeAgentProcessLauncher? launcher = null,
        TimeSpan? livenessWindow = null,
        LintReviewWindowOptions? reviewWindowOptions = null,
        Grimoire.Hub.Realtime.LintLifecyclePublisher? lifecyclePublisher = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-lint-lifecycle-{Guid.NewGuid():N}");
        var findingsDir = Path.Combine(root, "findings");
        Directory.CreateDirectory(findingsDir);

        var paths = new ResolvedGrimoirePaths(
            BaseDir: root,
            DataDir: root,
            ContentRoot: Path.Combine(root, "wiki"),
            TasksDir: Path.Combine(root, "wiki", "tasks"),
            IndexPath: Path.Combine(root, "wiki", "index.md"),
            LogPath: Path.Combine(root, "wiki", "log.md"),
            RawOriginalsDir: Path.Combine(root, "raw", "originals"),
            RawSourcesDir: Path.Combine(root, "raw", "sources"),
            StateDbPath: Path.Combine(root, "state.db"),
            SecretsFilePath: Path.Combine(root, ".env"),
            InstructionsDir: Path.Combine(root, "agents", "ingest"),
            SystemPromptPath: Path.Combine(root, "agents", "ingest", "system-prompt.md"),
            DefaultUserPromptPath: Path.Combine(root, "agents", "ingest", "default-user-prompt.md"),
            PolicyPath: Path.Combine(root, "agents", "ingest", "policy.json"),
            AgentWorkerPath: "unused",
            QueryInstructionsDir: Path.Combine(root, "agents", "query"),
            QuerySystemPromptPath: Path.Combine(root, "agents", "query", "system-prompt.md"),
            QueryPolicyPath: Path.Combine(root, "agents", "query", "policy.json"),
            ConversationsDir: Path.Combine(root, "conversations"),
            QueryAgentWorkerPath: "unused",
            WriteLocksDir: Path.Combine(root, "write-locks"),
            FindingsDir: findingsDir,
            LintInstructionsDir: Path.Combine(root, "agents", "lint"),
            LintSystemPromptPath: Path.Combine(root, "agents", "lint", "system-prompt.md"),
            LintPolicyPath: Path.Combine(root, "agents", "lint", "policy.json"),
            LintAgentWorkerPath: "unused",
            RemediationTasksDir: Path.Combine(root, "remediation-tasks"),
            LintPidPath: Path.Combine(root, "lint.pid"),
            Locations: []);

        var effectiveLauncher = launcher ?? new FakeAgentProcessLauncher(autoPlay: true);
        var reportStore = new FindingsReportStore(paths, NullLogger<FindingsReportStore>.Instance);
        var coordinator = new LintRunCoordinator(
            effectiveLauncher, reportStore, paths, livenessWindow: livenessWindow,
            reviewWindowOptions: reviewWindowOptions, logger: NullLogger<LintRunCoordinator>.Instance,
            lifecyclePublisher: lifecyclePublisher);

        return new LintCoordinatorHarness(root, paths, effectiveLauncher, coordinator);
    }

    public async Task<LintRunState> WaitForTerminalAsync(string runId)
    {
        await PollAsync.WaitAsync(
            () => Coordinator.GetRun(runId) is { Status: not LintRunStatus.Running },
            TimeSpan.FromSeconds(5),
            $"Expected lint run '{runId}' to reach a terminal status within 5s.");
        var run = Coordinator.GetRun(runId);

        Assert.NotNull(run);

        // The report write is a separate, slightly-later async step after the status
        // flips (LintRunCoordinator.FinishRunAsync) — poll for the file too.
        var reportPath = Paths.FindingsReportPathFor(runId);
        await PollAsync.WaitAsync(
            () => File.Exists(reportPath),
            TimeSpan.FromSeconds(5),
            $"Expected a Findings Report at '{reportPath}'.");

        Assert.True(File.Exists(reportPath), $"Expected a Findings Report at '{reportPath}'.");
        return run!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
