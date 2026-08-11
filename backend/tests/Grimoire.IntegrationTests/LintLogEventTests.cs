using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.LintAgent;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T028 (013-lint-agent, US1) — validates event name, level, and every mandatory field
/// for every <c>lint.*</c> structured log event declared in plan.md ## Observability >
/// Structured Log Events, mirroring QueryConversationLogEventTests.cs's/
/// QueryLifecycleLogEventTests.cs's pattern (shared <c>CaptureLogger&lt;T&gt;</c>
/// fixture, defined in IngestObservabilityLogTests.cs).
/// </summary>
public class LintLogEventTests
{
    [Fact]
    public void HubLintLifecycleEvents_EmitExpectedNamesLevelsAndFields()
    {
        var logger = new CaptureLogger<LintRunCoordinator>();

        LintLifecycleLogEvents.LogRunTriggered(logger, runId: "2026-07-30-lint-abc");
        LintLifecycleLogEvents.LogRunRejected(logger);
        LintLifecycleLogEvents.LogRunCompleted(logger, runId: "2026-07-30-lint-abc", findingsCount: 3);
        LintLifecycleLogEvents.LogRunFailed(logger, runId: "2026-07-30-lint-abc", reason: "liveness timeout");

        AssertEvent(logger.Entries, "lint.run.triggered", LogLevel.Information, ["run_id"]);
        AssertEvent(logger.Entries, "lint.run.rejected", LogLevel.Information, []);
        AssertEvent(logger.Entries, "lint.run.completed", LogLevel.Information, ["run_id", "findings_count"]);
        AssertEvent(logger.Entries, "lint.run.failed", LogLevel.Error, ["run_id", "reason"]);
    }

    [Fact]
    public void FindingsReportCreatedEvent_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<FindingsReportStore>();

        LintFindingsLogEvents.LogFindingsReportCreated(logger, runId: "2026-07-30-lint-abc", path: "/data/findings/2026-07-30-lint-abc.md");

        AssertEvent(logger.Entries, "lint.findings_report.created", LogLevel.Information, ["run_id", "path"]);
    }

    [Fact]
    public void AgentSideInstructionEvents_EmitExpectedNamesLevelsAndFields()
    {
        var logger = new CaptureLogger<LintLogEventTests>();

        LintAgentLogEvents.LogInstructionsLoaded(logger, runId: "run-1", systemPromptSha256: "abc", policyVersion: 1, policySha256: "def");
        LintAgentLogEvents.LogInstructionsLoadFailed(logger, runId: "run-1", reason: "not found");

        AssertEvent(logger.Entries, "lint.instructions.loaded", LogLevel.Information,
            ["run_id", "system_prompt_sha256", "policy_version", "policy_sha256"]);
        AssertEvent(logger.Entries, "lint.instructions.load_failed", LogLevel.Error, ["run_id", "reason"]);
    }

    [Fact]
    public void AgentSideSharedWriteConflictEvents_EmitExpectedNamesLevelsAndFields()
    {
        // ADR-015/ADR-016 (plan.md ## Observability note): Lint reuses the existing
        // wiki.write_conflict.rejected/wiki.write_lock.timeout signals for its own
        // out-of-scope write denials — no new event, extended reason enumeration only.
        var logger = new CaptureLogger<LintLogEventTests>();

        LintAgentLogEvents.LogWriteConflictRejected(logger, runId: "run-1", path: "/wiki/tech/a.md", reason: "frontmatter_only_body_changed", turn: 2);
        LintAgentLogEvents.LogWriteLockTimeout(logger, runId: "run-1", path: "/wiki/tech/a.md", waitMs: 5000);

        AssertEvent(logger.Entries, "wiki.write_conflict.rejected", LogLevel.Warning, ["task_id", "path", "reason", "turn"]);
        AssertEvent(logger.Entries, "wiki.write_lock.timeout", LogLevel.Warning, ["task_id", "path", "wait_ms"]);
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
