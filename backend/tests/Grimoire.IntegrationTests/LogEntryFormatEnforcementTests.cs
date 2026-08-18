using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Guardrails.Coordination;
using Grimoire.AgentRuntime.WikiLog;
using Grimoire.Domain.Guardrails;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 025-agent-owned-log T011 (ADR-028, amending ADR-017): <see cref="SharedFileWriteGuard"/>'s
/// log.md format-validation step — <b>prepend-only</b>, then heading shape
/// (<c>## [DATE] TYPE | SUMMARY</c>), then a following non-blank paragraph
/// (contracts/activity-log-write-contract.md) — hermetic against a real temp filesystem,
/// mirroring <c>SharedFileWriteGuardFrontmatterOnlyTests</c>'s idiom for ADR-016.
///
/// These cover Feature-Scoped Invariant FSI-1 the way Constitution Principle III requires:
/// classicist, state-based assertions over the real guard's returned decision and the real
/// bytes on disk — never by reflecting over the guard's shape. Deliberately agent-agnostic
/// throughout (including the trace span test below, which uses a private test-local
/// <see cref="ActivitySource"/> rather than any single agent's frozen tracing identity) —
/// the check applies uniformly to whichever agent process's <c>GuardedToolExecutor</c>
/// reaches it.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class LogEntryFormatEnforcementTests
{
    private static readonly ActivitySource TestActivitySource = new("LogEntryFormatEnforcementTests");

    private const string ConformingEntry =
        "## [2026-07-30] ingest | created retrieval-patterns\n\nCreated [[concepts/retrieval-patterns]] from source \"source.md\". Task: task-001.\n";

    private const string ExistingEntry =
        "## [2026-07-01] query | created single-composition-point\n\nEarlier entry. Ref: turn-000.\n";

    private static SharedFileWriteGuard NewGuard(string writeLocksDir, string logPath, ActivitySource? activitySource = null) =>
        new(writeLocksDir, backoffCap: TimeSpan.FromMilliseconds(500), logPath: logPath, activitySource: activitySource);

    /// <summary>FR-003, SC-004: the new entry goes on top and the prior bytes survive as an exact suffix.</summary>
    [Fact]
    public async Task WellFormedPrepend_ToExistingLog_Allows_AndKeepsPriorContentAsSuffix()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, ExistingEntry);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, ExistingEntry);

            var proposed = ConformingEntry + ExistingEntry;

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            await File.WriteAllTextAsync(logPath, proposed);
            guard.OnWriteCommitted(logPath, proposed);
            decision.LockHandle!.Dispose();

            var committed = await File.ReadAllTextAsync(logPath);
            Assert.StartsWith(ConformingEntry, committed, StringComparison.Ordinal);
            Assert.EndsWith(ExistingEntry, committed, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// FR-004, SC-001: the shape this feature replaced. Appending the new entry after the
    /// existing content — legal until ADR-028 — is now the canonical denial case.
    /// </summary>
    [Fact]
    public async Task OldAppendShape_Denies_LogEntryNotPrepended_AndLeavesFileUnchanged()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, ExistingEntry);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, ExistingEntry);

            var proposed = ExistingEntry + ConformingEntry;

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_not_prepended", decision.DenialReason);
            Assert.Null(decision.LockHandle);
            Assert.Equal(ExistingEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>FR-010, SC-004: a zero-byte file is a valid base, not a violation.</summary>
    [Fact]
    public async Task WellFormedFirstEntry_ToEmptyLog_Allows()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, string.Empty);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, string.Empty);

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, ConformingEntry, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>FR-005, SC-001: removing an existing entry is not a prepend.</summary>
    [Fact]
    public async Task PrependThatDropsAnExistingEntry_Denies_LogEntryNotPrepended()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var existing = ExistingEntry + "## [2026-06-01] ingest | created older\n\nOldest entry. Task: task-000.\n";
            await File.WriteAllTextAsync(logPath, existing);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, existing);

            // Keeps only the first existing entry below the new one — the oldest is dropped.
            var proposed = ConformingEntry + ExistingEntry;

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_not_prepended", decision.DenialReason);
            Assert.Equal(existing, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>FR-005, SC-001: reordering existing entries is not a prepend either.</summary>
    [Fact]
    public async Task PrependThatReordersExistingEntries_Denies_LogEntryNotPrepended()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            const string older = "## [2026-06-01] ingest | created older\n\nOldest entry. Task: task-000.\n";
            var existing = ExistingEntry + older;
            await File.WriteAllTextAsync(logPath, existing);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, existing);

            var proposed = ConformingEntry + older + ExistingEntry;

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_not_prepended", decision.DenialReason);
            Assert.Equal(existing, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Edge case: a prepend that also "fixes" an existing entry. The current content is no
    /// longer an exact suffix, so it is denied even though a conforming entry was added.
    /// </summary>
    [Fact]
    public async Task PrependThatAlsoEditsExistingContent_Denies_LogEntryNotPrepended()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, ExistingEntry);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, ExistingEntry);

            var proposed = ConformingEntry
                + ExistingEntry.Replace("Earlier entry", "Corrected entry", StringComparison.Ordinal);

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_not_prepended", decision.DenialReason);
            Assert.Equal(ExistingEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// FR-006, FR-009, SC-003: a same-day second entry is an ordinary prepend, and two
    /// entries may carry byte-identical headings. The harness has no "day section" concept.
    /// </summary>
    [Fact]
    public async Task SameDayAndDuplicateHeadingPrepends_AreAllowed_AndBothRemainLocatable()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, ConformingEntry);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, ConformingEntry);

            // Byte-identical heading, same date — nothing treats the heading as a key.
            var proposed = ConformingEntry + ConformingEntry;

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            await File.WriteAllTextAsync(logPath, proposed);
            guard.OnWriteCommitted(logPath, proposed);
            decision.LockHandle!.Dispose();

            var committed = await File.ReadAllTextAsync(logPath);
            var matches = Regex.Matches(committed, @"^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$", RegexOptions.Multiline);
            Assert.Equal(2, matches.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WellFormedFirstEntry_ToNonExistentLog_Allows()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, ConformingEntry, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InPlaceRewrite_Denies_LogEntryNotPrepended()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, ExistingEntry);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, ExistingEntry);

            // Rewrites the existing entry's text instead of prepending to it.
            var proposed = ExistingEntry.Replace("Earlier entry", "Rewritten entry", StringComparison.Ordinal);

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_not_prepended", decision.DenialReason);
            Assert.Null(decision.LockHandle);

            // Nothing was applied — the on-disk file is untouched.
            Assert.Equal(ExistingEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Edge case: a head of pure whitespace above intact existing content satisfies the
    /// prepend rule but carries no entry, so the heading check rejects it.
    /// </summary>
    [Fact]
    public async Task WhitespaceOnlyPrepend_Denies_LogEntryMalformedHeading()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, ExistingEntry);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, ExistingEntry);

            var proposed = "\n\n" + ExistingEntry;

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_malformed_heading", decision.DenialReason);
            Assert.Equal(ExistingEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Edge case: a conforming heading prepended with no paragraph before the existing
    /// content resumes is rejected within the head, not excused by the content below.
    /// </summary>
    [Fact]
    public async Task HeadingOnlyPrepend_AboveExistingContent_Denies_LogEntryMissingParagraph()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, ExistingEntry);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, ExistingEntry);

            var proposed = "## [2026-07-30] ingest | created retrieval-patterns\n\n" + ExistingEntry;

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_missing_paragraph", decision.DenialReason);
            Assert.Equal(ExistingEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedHeading_Denies_LogEntryMalformedHeading()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            const string existing = "";
            await File.WriteAllTextAsync(logPath, existing);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, existing);

            // No "## [DATE] TYPE | SUMMARY" heading at all.
            var proposed = "Just a note about what happened, no heading.\n";

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_malformed_heading", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HeadingMissingDateBracketsAndPipe_Denies_LogEntryMalformedHeading()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            const string existing = "";
            await File.WriteAllTextAsync(logPath, existing);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, existing);

            // No "[DATE]" bracket, no " | " separator — the pattern requires both.
            var proposed = "## 2026-07-30 ingest completed\n\nSome paragraph.\n";

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_malformed_heading", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WrongHeadingLevel_Denies_LogEntryMalformedHeading()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            const string existing = "";
            await File.WriteAllTextAsync(logPath, existing);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, existing);

            // "#" (single hash) — the contract requires exactly "##" level.
            var proposed = "# [2026-07-30] ingest | completed\n\nSome paragraph.\n";

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_malformed_heading", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HeadingWithNoFollowingParagraph_Denies_LogEntryMissingParagraph()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            const string existing = "";
            await File.WriteAllTextAsync(logPath, existing);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, existing);

            var proposed = "## [2026-07-30] ingest | completed\n\n";

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_missing_paragraph", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HeadingImmediatelyFollowedByBlankLineOnly_Denies_LogEntryMissingParagraph()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            const string existing = "";
            await File.WriteAllTextAsync(logPath, existing);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, existing);

            var proposed = "## [2026-07-30] ingest | completed\n\n   \n";

            var decision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("log_entry_missing_paragraph", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteToOtherTarget_IsNeverFormatChecked_EvenWithLogPathConfigured()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            var otherTarget = Path.Combine(root, "wiki", "concepts", "example.md");
            Directory.CreateDirectory(Path.GetDirectoryName(otherTarget)!);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);

            // Content that would fail every log.md format check, written to a different path.
            var decision = await guard.EvaluateWriteAsync(otherTarget, WriteMode.ReadWrite, "not a log entry at all", CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 025-agent-owned-log SC-003/FR-009 (was 014's SC-004): every entry in
    /// <c>log.md</c> remains locatable by searching the file for the heading pattern,
    /// however many entries accumulate. Every entry is now agent-written through the
    /// guard — the backstop that wrote a third entry directly is deleted (FR-001,
    /// FR-002), so this test's third entry is a third guarded prepend rather than a
    /// harness append.
    /// </summary>
    [Fact]
    public async Task MultiEntryLog_EveryEntryLocatableByHeadingPattern()
    {
        var root = CreateTempDir();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.WriteAllTextAsync(logPath, string.Empty);

            var guard = NewGuard(Path.Combine(root, "write-locks"), logPath);
            guard.OnReadFile(logPath, string.Empty);

            var firstEntry = "## [2026-07-28] ingest | completed\n\nAgent-authored entry one. Ref: task-001.\n";
            var firstDecision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, firstEntry, CancellationToken.None);
            Assert.True(firstDecision.IsAllowed);
            await File.WriteAllTextAsync(logPath, firstEntry);
            guard.OnWriteCommitted(logPath, firstEntry);
            firstDecision.LockHandle!.Dispose();

            var afterSecond = "## [2026-07-29] query | completed\n\nAgent-authored entry two. Ref: turn-002.\n" + firstEntry;
            var secondDecision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, afterSecond, CancellationToken.None);
            Assert.True(secondDecision.IsAllowed);
            await File.WriteAllTextAsync(logPath, afterSecond);
            guard.OnWriteCommitted(logPath, afterSecond);
            secondDecision.LockHandle!.Dispose();

            var afterThird = "## [2026-07-30] ingest | superseded\n\nAgent-authored entry three. Ref: task-003.\n" + afterSecond;
            var thirdDecision = await guard.EvaluateWriteAsync(logPath, WriteMode.ReadWrite, afterThird, CancellationToken.None);
            Assert.True(thirdDecision.IsAllowed);
            await File.WriteAllTextAsync(logPath, afterThird);
            guard.OnWriteCommitted(logPath, afterThird);
            thirdDecision.LockHandle!.Dispose();

            var content = await File.ReadAllTextAsync(logPath);
            var matches = Regex.Matches(content, @"^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$", RegexOptions.Multiline);

            Assert.Equal(3, matches.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// T037: the <c>guardrails.format_validate</c> span exists with the plan-mandated
    /// attributes. It is created inside <see cref="SharedFileWriteGuard.EvaluateWriteAsync(string, WriteMode, string, CancellationToken)"/>
    /// itself (ADR-017's mechanism lives there, run after the existing checks) — the same
    /// call GuardedToolExecutor wraps in its own <c>guardrails.acquire_write_lock</c> span
    /// while holding the per-target lock (contract §3), so in practice this span nests
    /// under <c>guardrails.acquire_write_lock</c>, not literally under
    /// <c>{ingest,query,lint}_agent.tool_call</c> (which, by the pre-existing
    /// RecordAllowed/RecordDenied contract, is only created afterward once the write's
    /// final decision is known — the same documented timing constraint
    /// <c>QueryWriteLockObservabilityTests</c> already notes for
    /// <c>guardrails.acquire_write_lock</c>'s own real-vs-planned parent). This test uses
    /// a neutral ambient span (rather than any single agent's own
    /// <c>guardrails.acquire_write_lock</c>, which requires that agent's
    /// <c>IToolCallInstrumentation</c>) to stay agent-agnostic while still proving the
    /// mechanism: the span nests under whatever is <see cref="Activity.Current"/> at
    /// format-check time, on whichever <see cref="ActivitySource"/> the caller supplied.
    /// </summary>
    [Fact]
    public async Task FormatValidateSpan_EmittedWithExpectedAttributes_NestedUnderAmbientActivity()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "LogEntryFormatEnforcementTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var root = CreateTempDir();
        try
        {
            var wikiDir = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiDir);
            var logPath = Path.Combine(wikiDir, "log.md");
            await File.WriteAllTextAsync(logPath, string.Empty);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
                writePrefixes: [wikiDir + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, root, taskId: "task-format-span",
                writeLocksDir: Path.Combine(root, "write-locks"),
                logPath: logPath,
                activitySource: TestActivitySource);

            Activity? ambient;
            using (ambient = TestActivitySource.StartActivity("test.ambient"))
            {
                // Seed the write guard's compare-and-swap baseline first (read-before-write,
                // same contract every agent's own log.md upkeep follows) — otherwise the
                // write is denied earlier as write_conflict_stale_read, never reaching the
                // format-validation step under test.
                await executor.ExecuteAsync("read_file", """{"path": "wiki/log.md"}""", turn: 1, CancellationToken.None);

                var result = await executor.ExecuteAsync(
                    "write_file",
                    $$"""{"path": "wiki/log.md", "content": "not a conforming log entry"}""",
                    turn: 2,
                    CancellationToken.None);

                Assert.True(result.IsError);
            }

            // Other test classes may run in parallel and emit spans on the same
            // ActivitySource; only this test's own trace is under assertion (mirrors
            // AgenticIngestTraceSpans_EmitExpectedHierarchyAndAttributes's idiom).
            var thisTrace = activities.Where(a => a.TraceId == ambient!.TraceId).ToList();
            var formatSpan = thisTrace.Single(a => a.OperationName == "guardrails.format_validate");

            Assert.Equal(ambient!.SpanId.ToHexString(), formatSpan.ParentSpanId.ToHexString());
            Assert.Equal(logPath, GetTag(formatSpan, "path"));
            Assert.Equal("log", GetTag(formatSpan, "target"));
            Assert.Equal("denied", GetTag(formatSpan, "outcome"));
            Assert.Equal("log_entry_malformed_heading", GetTag(formatSpan, "reason"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 025-agent-owned-log T013 (FR-004, SC-001): the <c>guardrails.format_validate</c>
    /// span is unchanged in name, parentage, and attribute set — only the <c>reason</c>
    /// value it carries on an ordering denial changes, from <c>log_entry_not_appended</c>
    /// to <c>log_entry_not_prepended</c>. Asserted through the real executor and guard, on
    /// the shape this feature made illegal: appending the new entry after the existing
    /// content.
    /// </summary>
    [Fact]
    public async Task FormatValidateSpan_OnOrderingDenial_CarriesLogEntryNotPrependedReason()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            // Literal name, not TestActivitySource.Name: this lambda can run while the
            // class's static initializer is still constructing that very field.
            ShouldListenTo = src => src.Name == "LogEntryFormatEnforcementTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);

        var root = CreateTempDir();
        try
        {
            var wikiDir = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiDir);
            var logPath = Path.Combine(wikiDir, "log.md");
            await File.WriteAllTextAsync(logPath, ExistingEntry);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
                writePrefixes: [wikiDir + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, root, taskId: "task-format-span-prepend",
                writeLocksDir: Path.Combine(root, "write-locks"),
                logPath: logPath,
                activitySource: TestActivitySource);

            Activity? ambient;
            using (ambient = TestActivitySource.StartActivity("test.ambient"))
            {
                await executor.ExecuteAsync("read_file", """{"path": "wiki/log.md"}""", turn: 1, CancellationToken.None);

                // The old append shape: existing content first, new entry at the end.
                var appended = System.Text.Json.JsonSerializer.Serialize(ExistingEntry + ConformingEntry);
                var result = await executor.ExecuteAsync(
                    "write_file",
                    $$"""{"path": "wiki/log.md", "content": {{appended}}}""",
                    turn: 2,
                    CancellationToken.None);

                Assert.True(result.IsError);
            }

            var thisTrace = activities.Where(a => a.TraceId == ambient!.TraceId).ToList();
            var formatSpan = thisTrace.Single(a => a.OperationName == "guardrails.format_validate");

            Assert.Equal(logPath, GetTag(formatSpan, "path"));
            Assert.Equal("log", GetTag(formatSpan, "target"));
            Assert.Equal("denied", GetTag(formatSpan, "outcome"));
            Assert.Equal("log_entry_not_prepended", GetTag(formatSpan, "reason"));

            // The denial left the file untouched.
            Assert.Equal(ExistingEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString() ?? string.Empty;

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"log-entry-format-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
