using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T047 (026-guarded-tool-surface, US2, Principle IV): validates the
/// <c>guardrails.delete_file</c> span's name, parent linkage and <c>task_id</c>
/// correlation, read from the production composition root — the real
/// <c>Grimoire.LintAgent</c> <see cref="ActivitySource"/> and the real
/// <see cref="LintToolCallInstrumentation"/> adapter, driven end to end through
/// <see cref="AgentLoop"/>, exactly like <c>LintSearchObservabilityTests</c>'s pattern.
///
/// T046's log-event half (name/level/mandatory fields for
/// <c>wiki.page.deleted</c>/<c>wiki.page.delete_rolled_back</c>) is already covered by
/// <c>LintLogEventTests.GuardedRetrievalEvents_EmitExpectedNamesLevelsAndFields</c>,
/// added in this feature's foundational layer — not duplicated here.
/// </summary>
public class LintDeletionObservabilityTests
{
    private static readonly ToolRegistry FullScopeRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.DeleteFileDefinition,
    ]);

    [Fact]
    public async Task DeleteFileSpan_IsChildOfModelTurn_AndCorrelatesByRunId()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.LintAgent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var root = Path.Combine(Path.GetTempPath(), $"lint-deletion-observability-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(root, "wiki");
        Directory.CreateDirectory(Path.Combine(wikiDir, "tech"));
        var pagePath = Path.Combine(wikiDir, "tech", "obsolete.md");
        await File.WriteAllTextAsync(pagePath, "obsolete content");

        try
        {
            var policy = new SafetyPolicy(
                wikiDir,
                readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiDir + Path.DirectorySeparatorChar, WriteMode.ReadWrite)],
                deleteRules: [new DeleteRule(wikiDir + Path.DirectorySeparatorChar)]);
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiDir, taskId: "run-delete-obs-1",
                registry: FullScopeRegistry,
                instrumentation: new LintToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
            var fakeModel = new FakeModelClient([
                FakeModelClient.ToolCallTurn("tool-1", ToolRegistry.DeleteFile, """{"path": "tech/obsolete.md"}"""),
                FakeModelClient.FinalTurn("Deleted the obsolete page."),
            ]);
            var loop = new AgentLoop(
                fakeModel, executor,
                registry: FullScopeRegistry,
                instrumentation: new LintAgentLoopInstrumentation());

            using (LintAgentTracing.StartRunActivity("run-delete-obs-1"))
            {
                await loop.RunAsync(
                    "You are a test lint agent.",
                    [new ConversationMessage("user", "Perform the wiki health check now.")],
                    "run-delete-obs-1", CancellationToken.None);
            }

            var run = Assert.Single(activities.Where(a => a.OperationName == "lint_agent.run"));
            var all = activities.Where(a => a.TraceId == run.TraceId).ToList();
            var modelTurns = all.Where(a => a.OperationName == "lint_agent.model_turn").ToList();
            var deleteSpan = Assert.Single(all.Where(a => a.OperationName == "guardrails.delete_file"));

            var toolTurn = modelTurns.Single(t => GetTag(t, "stop_reason") == "tool_use");
            Assert.Equal(toolTurn.SpanId.ToHexString(), deleteSpan.ParentSpanId.ToHexString());
            Assert.Equal("run-delete-obs-1", GetTag(deleteSpan, "task_id"));

            Assert.Equal("applied", GetTag(deleteSpan, "outcome"));
            Assert.Equal("True", GetTag(deleteSpan, "journaled"));
            Assert.False(File.Exists(pagePath));
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
