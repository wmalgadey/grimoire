using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T030/T031 (026-guarded-tool-surface, US1, Principle IV): validates the
/// <c>guardrails.search_scan</c> span's name, parent linkage and <c>task_id</c>
/// correlation, read from the production composition root — the real
/// <c>Grimoire.LintAgent</c> <see cref="ActivitySource"/> and the real
/// <see cref="LintToolCallInstrumentation"/> adapter, driven end to end through
/// <see cref="AgentLoop"/>, exactly like <c>LintTraceTests</c>'s existing pattern.
///
/// T030's log-event half (name/level/mandatory fields for
/// <c>wiki.search.truncated</c>/<c>timed_out</c>/<c>pattern_rejected</c>) is already
/// covered by <c>LintLogEventTests.GuardedRetrievalEvents_EmitExpectedNamesLevelsAndFields</c>,
/// added in this feature's foundational layer — not duplicated here.
/// </summary>
public class LintSearchObservabilityTests
{
    private static readonly ToolRegistry SearchCapableRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.SearchFilesDefinition,
    ]);

    [Fact]
    public async Task SearchScanSpan_IsChildOfModelTurn_AndCorrelatesByRunId()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.LintAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"lint-search-observability-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiDir);
        await File.WriteAllTextAsync(Path.Combine(wikiDir, "page.md"), "the target term appears here");

        try
        {
            var policy = new SafetyPolicy(wikiDir, readPrefixes: [wikiDir + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiDir, taskId: "run-search-obs-1",
                registry: SearchCapableRegistry,
                instrumentation: new LintToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
            var fakeModel = new FakeModelClient([
                FakeModelClient.ToolCallTurn("tool-1", ToolRegistry.SearchFiles, """{"pattern": "target term"}"""),
                FakeModelClient.FinalTurn("Found it."),
            ]);
            var loop = new AgentLoop(
                fakeModel, executor,
                registry: SearchCapableRegistry,
                instrumentation: new LintAgentLoopInstrumentation());

            using (LintAgentTracing.StartRunActivity("run-search-obs-1"))
            {
                await loop.RunAsync(
                    "You are a test lint agent.",
                    [new ConversationMessage("user", "Perform the wiki health check now.")],
                    "run-search-obs-1", CancellationToken.None);
            }

            var run = Assert.Single(activities.Where(a => a.OperationName == "lint_agent.run"));
            var all = activities.Where(a => a.TraceId == run.TraceId).ToList();
            var modelTurns = all.Where(a => a.OperationName == "lint_agent.model_turn").ToList();
            var searchScan = Assert.Single(all.Where(a => a.OperationName == "guardrails.search_scan"));

            var toolTurn = modelTurns.Single(t => GetTag(t, "stop_reason") == "tool_use");
            Assert.Equal(toolTurn.SpanId.ToHexString(), searchScan.ParentSpanId.ToHexString());
            Assert.Equal("run-search-obs-1", GetTag(searchScan, "task_id"));

            Assert.Equal("completed", GetTag(searchScan, "outcome"));
            Assert.Equal("1", GetTag(searchScan, "matches"));
            Assert.Equal("1", GetTag(searchScan, "files_scanned"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;
}
