using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T062 (026-guarded-tool-surface, US4, Principle IV): validates the
/// <c>guardrails.batch</c> span's name, parent linkage (<c>lint_agent.model_turn</c>,
/// plan.md ## Observability), and its <c>call_count</c>/<c>denied_count</c> attributes,
/// read from the production composition root — the real <c>Grimoire.LintAgent</c>
/// <see cref="ActivitySource"/> and the real <see cref="LintToolCallInstrumentation"/>
/// adapter, driven end to end through <see cref="AgentLoop"/>, mirroring
/// <c>LintSearchObservabilityTests</c>'/<c>LintDeletionObservabilityTests</c>' pattern.
///
/// <c>wiki.batch.rejected</c>'s log-event half (name/level/mandatory fields) is already
/// covered by <c>LintLogEventTests</c> — not duplicated here.
/// </summary>
public class LintBatchObservabilityTests
{
    private static readonly ToolRegistry BatchCapableRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.SearchFilesDefinition,
        ToolRegistry.BatchDefinition,
    ]);

    [Fact]
    public async Task BatchSpan_IsChildOfModelTurn_WithCallCountAndDeniedCount()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.LintAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"lint-batch-observability-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        var techDir = Path.Combine(wikiDir, "tech");
        var secretDir = Path.Combine(wikiDir, "secret");
        Directory.CreateDirectory(techDir);
        Directory.CreateDirectory(secretDir);
        await File.WriteAllTextAsync(Path.Combine(techDir, "page.md"), "allowed content");
        await File.WriteAllTextAsync(Path.Combine(secretDir, "hidden.md"), "denied content");

        try
        {
            var policy = new SafetyPolicy(wikiDir, readPrefixes: [techDir + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiDir, taskId: "run-batch-obs-1",
                registry: BatchCapableRegistry,
                instrumentation: new LintToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
            var fakeModel = new FakeModelClient([
                FakeModelClient.ToolCallTurn(
                    "tool-1", ToolRegistry.Batch,
                    """{"calls": [{"tool": "read_file", "input": {"path": "tech/page.md"}}, {"tool": "read_file", "input": {"path": "secret/hidden.md"}}]}"""),
                FakeModelClient.FinalTurn("Done."),
            ]);
            var loop = new AgentLoop(
                fakeModel, executor,
                registry: BatchCapableRegistry,
                instrumentation: new LintAgentLoopInstrumentation());

            using (LintAgentTracing.StartRunActivity("run-batch-obs-1"))
            {
                await loop.RunAsync(
                    "You are a test lint agent.",
                    [new ConversationMessage("user", "Perform the wiki health check now.")],
                    "run-batch-obs-1", CancellationToken.None);
            }

            var run = Assert.Single(activities.Where(a => a.OperationName == "lint_agent.run"));
            var all = activities.Where(a => a.TraceId == run.TraceId).ToList();
            var modelTurns = all.Where(a => a.OperationName == "lint_agent.model_turn").ToList();
            var batchSpan = Assert.Single(all.Where(a => a.OperationName == "guardrails.batch"));

            var toolTurn = modelTurns.Single(t => GetTag(t, "stop_reason") == "tool_use");
            Assert.Equal(toolTurn.SpanId.ToHexString(), batchSpan.ParentSpanId.ToHexString());
            Assert.Equal("run-batch-obs-1", GetTag(batchSpan, "task_id"));

            Assert.Equal("2", GetTag(batchSpan, "call_count"));
            Assert.Equal("1", GetTag(batchSpan, "denied_count"));
            Assert.Equal("completed", GetTag(batchSpan, "outcome"));
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
