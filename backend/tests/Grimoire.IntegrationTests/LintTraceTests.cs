using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T030 (013-lint-agent, US1, mirrors QueryLifecycleTraceTests.cs) — validates span
/// names, parent/child linkage, and <c>run_id</c> correlation for the Lint trace spans
/// declared in plan.md ## Observability > Distributed Trace Spans, split into the
/// Hub-side subtree (<c>hub.lint.trigger</c> → <c>hub.lint.run_supervision</c> →
/// <c>hub.lint.write_findings_report</c>) and the agent-process-side subtree
/// (<c>lint_agent.run</c> → <c>lint_agent.load_instructions</c>/<c>lint_agent.tool_call</c>),
/// since the two are separate processes/ActivitySources in production.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class LintTraceTests
{
    [Fact]
    public async Task HubLintSpans_EmitExpectedHierarchy_ForOneCompletedRun()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using var harness = LintCoordinatorHarness.Create();
        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<Grimoire.Hub.LintDispatch.LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        await harness.WaitForTerminalAsync(runId);

        var trigger = Assert.Single(activities.Where(a => a.OperationName == "hub.lint.trigger" && GetTag(a, "run_id") == runId));
        var supervision = Assert.Single(activities.Where(a => a.OperationName == "hub.lint.run_supervision" && GetTag(a, "run_id") == runId));
        var writeReport = Assert.Single(activities.Where(a => a.OperationName == "hub.lint.write_findings_report" && GetTag(a, "run_id") == runId));

        Assert.Equal("accepted", GetTag(trigger, "outcome"));
        // hub.lint.run_supervision and hub.lint.write_findings_report are both started
        // from LintRunCoordinator's own async flow rather than nested inside the trigger
        // span's using-block (the trigger span closes once dispatch returns) — correlated
        // instead by the shared run_id tag, matching every span's assertion above.
        Assert.Equal("completed", GetTag(supervision, "outcome"));
        Assert.NotEmpty(GetTag(writeReport, "path"));
    }

    [Fact]
    public async Task HubLintSpans_LivenessFailure_MarksSupervisionOutcomeAndStillWritesAPartialReport()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.Hub",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        using var harness = LintCoordinatorHarness.Create(
            new FakeAgentProcessLauncher(autoPlay: false), livenessWindow: TimeSpan.FromMilliseconds(100));
        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<Grimoire.Hub.LintDispatch.LintSubmissionResult.Accepted>(result);
        var runId = accepted.Run.RunId;

        await harness.WaitForTerminalAsync(runId);

        var supervision = Assert.Single(activities.Where(a => a.OperationName == "hub.lint.run_supervision" && GetTag(a, "run_id") == runId));
        Assert.Equal("liveness_failed", GetTag(supervision, "outcome"));

        var content = await File.ReadAllTextAsync(harness.Paths.FindingsReportPathFor(runId));
        Assert.Contains("partial: true", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LintAgentSpans_EmitExpectedHierarchyAndAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.LintAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"lint-agent-trace-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var techDir = Path.Combine(wikiDir, "tech");
        Directory.CreateDirectory(techDir);
        await File.WriteAllTextAsync(Path.Combine(techDir, "adr.md"), "---\ntype: Technology\n---\n# ADR notes");

        var policy = new SafetyPolicy(
            wikiDir,
            readPrefixes: [techDir + Path.DirectorySeparatorChar],
            writeRules: [new WriteRule(techDir + Path.DirectorySeparatorChar, WriteMode.FrontmatterOnly)]);
        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            policy, journal, wikiDir, taskId: "run-trace-1",
            registry: LintToolRegistry.Default,
            instrumentation: new LintToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
        var fakeModel = new FakeModelClient([
            FakeModelClient.ReadFileTurn("tool-1", "tech/adr.md"),
            FakeModelClient.FinalTurn("## Content Quality\n\nNo content-quality findings.\n")]);
        var loop = new AgentLoop(
            fakeModel, executor,
            registry: LintToolRegistry.Default,
            instrumentation: new LintAgentLoopInstrumentation());

        using (var runSpan = LintAgentTracing.StartRunActivity("run-trace-1"))
        {
            using (var loadSpan = LintAgentTracing.ActivitySource.StartActivity("lint_agent.load_instructions"))
            {
                loadSpan?.SetTag("run_id", "run-trace-1");
                loadSpan?.SetTag("system_prompt_sha256", "abc123");
            }

            await loop.RunAsync("You are a test lint agent.", [new ConversationMessage("user", "Perform the wiki health check now.")],
                "run-trace-1", CancellationToken.None);
        }

        var run = Assert.Single(activities.Where(a => a.OperationName == "lint_agent.run"));
        var all = activities.Where(a => a.TraceId == run.TraceId).ToList();
        var load = Assert.Single(all.Where(a => a.OperationName == "lint_agent.load_instructions"));
        var turns = all.Where(a => a.OperationName == "lint_agent.model_turn").ToList();
        var tool = Assert.Single(all.Where(a => a.OperationName == "lint_agent.tool_call"));

        Assert.Equal(2, turns.Count);
        Assert.Equal(run.SpanId.ToHexString(), load.ParentSpanId.ToHexString());
        Assert.All(turns, a => Assert.Equal(run.SpanId.ToHexString(), a.ParentSpanId.ToHexString()));
        var toolTurn = turns.Single(t => GetTag(t, "stop_reason") == "tool_use");
        Assert.Equal(toolTurn.SpanId.ToHexString(), tool.ParentSpanId.ToHexString());
        Assert.Equal("allowed", GetTag(tool, "decision"));
        Assert.Equal("read_file", GetTag(tool, "tool"));
        Assert.Equal("run-trace-1", GetTag(run, "run_id"));
        Assert.Equal("run-trace-1", GetTag(tool, "run_id"));
    }

    [Fact]
    public async Task LintAgentToolCallSpan_ReflectsDeniedDecision_ForABodyChangingWrite()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.LintAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"lint-agent-trace-denied-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var techDir = Path.Combine(wikiDir, "tech");
        Directory.CreateDirectory(techDir);
        var pagePath = Path.Combine(techDir, "a.md");
        await File.WriteAllTextAsync(pagePath, "---\ntype: Technology\n---\nOriginal body.");

        var policy = new SafetyPolicy(
            wikiDir,
            readPrefixes: [techDir + Path.DirectorySeparatorChar],
            writeRules: [new WriteRule(techDir + Path.DirectorySeparatorChar, WriteMode.FrontmatterOnly)]);
        var journal = new WriteJournal();
        var writeLocksDir = Path.Combine(root, "write-locks");
        var executor = new GuardedToolExecutor(
            policy, journal, wikiDir, taskId: "run-trace-denied-1",
            registry: LintToolRegistry.Default,
            instrumentation: new LintToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance),
            writeLocksDir: writeLocksDir);
        var fakeModel = new FakeModelClient([
            FakeModelClient.ReadFileTurn("tool-1", "tech/a.md"),
            FakeModelClient.WriteFileTurn("tool-2", "tech/a.md", "---\ntype: Technology\n---\nRewritten body!"),
            FakeModelClient.FinalTurn("Done.")]);
        var loop = new AgentLoop(
            fakeModel, executor,
            registry: LintToolRegistry.Default,
            instrumentation: new LintAgentLoopInstrumentation());

        using (LintAgentTracing.StartRunActivity("run-trace-denied-1"))
        {
            await loop.RunAsync("You are a test lint agent.", [new ConversationMessage("user", "Perform the wiki health check now.")],
                "run-trace-denied-1", CancellationToken.None);
        }

        var run = Assert.Single(activities.Where(a => a.OperationName == "lint_agent.run"));
        var tool = Assert.Single(activities.Where(a => a.OperationName == "lint_agent.tool_call" && a.TraceId == run.TraceId && GetTag(a, "tool") == "write_file"));

        Assert.Equal("denied", GetTag(tool, "decision"));
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
