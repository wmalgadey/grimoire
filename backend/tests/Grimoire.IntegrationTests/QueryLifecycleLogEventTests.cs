using Grimoire.Hub.QueryDispatch;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T043 (US1, mirrors IngestLifecycleLogEventTests.cs) — validates event name, level,
/// and mandatory fields for the Query structured log events declared in plan.md
/// ## Observability > Structured Log Events (008-query-agent).
/// </summary>
public class QueryLifecycleLogEventTests
{
    [Fact]
    public void HubSideQueryStructuredEvents_EmitExpectedNamesLevelsAndFields()
    {
        var logger = new CaptureLogger<QueryLifecycleLogEventTests>();

        QueryLifecycleLogEvents.LogTurnCreated(logger, conversationId: "c-1", turnId: "t-1");
        QueryLifecycleLogEvents.LogTurnCompleted(logger, turnId: "t-1", durationMs: 1234);
        QueryLifecycleLogEvents.LogTurnFailed(logger, turnId: "t-2", reason: "liveness timeout");
        QueryLifecycleLogEvents.LogSubmissionRejected(logger, conversationId: "c-3");

        AssertEvent(logger.Entries, "query.turn.created", LogLevel.Information, ["conversation_id", "turn_id"]);
        AssertEvent(logger.Entries, "query.turn.completed", LogLevel.Information, ["turn_id", "duration_ms"]);
        AssertEvent(logger.Entries, "query.turn.failed", LogLevel.Error, ["turn_id", "reason"]);
        AssertEvent(logger.Entries, "query.submission.rejected", LogLevel.Information, ["conversation_id"]);
    }

    [Fact]
    public void AgentSideQueryStructuredEvents_EmitExpectedNamesLevelsAndFields()
    {
        var logger = new CaptureLogger<QueryLifecycleLogEventTests>();

        QueryAgentLogEvents.LogInstructionsLoaded(logger, turnId: "t-4", systemPromptSha256: "abc123", policyVersion: 1, policySha256: "def456");
        QueryAgentLogEvents.LogInstructionsLoadFailed(logger, turnId: "t-5", reason: "missing file");
        QueryAgentLogEvents.LogToolDenied(logger, turnId: "t-6", tool: "read_file", target: "../secret.txt", reason: "outside scope", turn: 2);

        AssertEvent(logger.Entries, "query.instructions.loaded", LogLevel.Information,
            ["turn_id", "system_prompt_sha256", "policy_version", "policy_sha256"]);
        AssertEvent(logger.Entries, "query.instructions.load_failed", LogLevel.Error, ["turn_id", "reason"]);
        AssertEvent(logger.Entries, "query.tool.denied", LogLevel.Warning, ["turn_id", "tool", "target", "reason", "turn"]);
    }

    private static void AssertEvent(
        List<CaptureLoggerEntry> entries,
        string eventName,
        LogLevel level,
        string[] requiredFields)
    {
        var entry = Assert.Single(entries.Where(e => e.EventName == eventName));
        Assert.Equal(level, entry.Level);

        foreach (var field in requiredFields)
        {
            Assert.True(entry.Fields.ContainsKey(field), $"Missing field '{field}' for event '{eventName}'.");
        }
    }
}
