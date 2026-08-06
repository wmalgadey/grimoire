using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Guardrails.Coordination;
using Grimoire.Domain.Guardrails;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T044 (014-wiki-storage-restructure, US4, ADR-017): <see cref="SharedFileWriteGuard"/>'s
/// new index.md format-validation step — every <c>- [</c>-led line present in the
/// proposed content but absent, byte-for-byte, from the current content must match
/// <c>^- \[.+\]\(.+\) — .+ — .+$</c> (contracts/log-and-catalog-entry-format.md) —
/// hermetic against a real temp filesystem, mirroring
/// <see cref="LogEntryFormatEnforcementTests"/>'s idiom exactly, extended for US4's
/// index.md target. Written before T046 lands (Red half of ADR-017's US4 probe): every
/// deny-path test here fails until <see cref="SharedFileWriteGuard.EvaluateWriteAsync(string, WriteMode, string, CancellationToken)"/>
/// gains the index.md format check. Deliberately agent-agnostic throughout (including
/// the trace span test below) — ADR-017's check applies uniformly to whichever agent
/// process's <c>GuardedToolExecutor</c> reaches it.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class CatalogEntryFormatEnforcementTests
{
    private static readonly ActivitySource TestActivitySource = new("CatalogEntryFormatEnforcementTests");

    private const string ExistingCatalog =
        "# Wiki Index\n\n## Concepts\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — Beschreibt Muster gegen Kaskadenausfälle — 3 Quellen\n";

    private static SharedFileWriteGuard NewGuard(string writeLocksDir, string indexPath, ActivitySource? activitySource = null) =>
        new(writeLocksDir, backoffCap: TimeSpan.FromMilliseconds(500), indexPath: indexPath, activitySource: activitySource);

    [Fact]
    public async Task WellFormedNewCatalogLine_ToExistingIndex_Allows()
    {
        var root = CreateTempDir();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await File.WriteAllTextAsync(indexPath, ExistingCatalog);

            var guard = NewGuard(Path.Combine(root, "write-locks"), indexPath);
            guard.OnReadFile(indexPath, ExistingCatalog);

            var proposed = ExistingCatalog +
                "- [Retry Backoff](concepts/retry-backoff.md) — Beschreibt exponentielles Backoff bei Wiederholungen — 2 Quellen\n";

            var decision = await guard.EvaluateWriteAsync(indexPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WellFormedFirstCatalogLine_ToNonExistentIndex_Allows()
    {
        var root = CreateTempDir();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");

            var guard = NewGuard(Path.Combine(root, "write-locks"), indexPath);

            var proposed = "# Wiki Index\n\n## Concepts\n\n" +
                "- [Circuit Breaker](concepts/circuit-breaker.md) — Beschreibt Muster gegen Kaskadenausfälle — 3 Quellen\n";

            var decision = await guard.EvaluateWriteAsync(indexPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedNewCatalogLine_MissingStatusMarker_Denies_CatalogEntryMalformed()
    {
        var root = CreateTempDir();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await File.WriteAllTextAsync(indexPath, ExistingCatalog);

            var guard = NewGuard(Path.Combine(root, "write-locks"), indexPath);
            guard.OnReadFile(indexPath, ExistingCatalog);

            // Missing the trailing " — status" segment.
            var proposed = ExistingCatalog +
                "- [Retry Backoff](concepts/retry-backoff.md) — Beschreibt exponentielles Backoff bei Wiederholungen\n";

            var decision = await guard.EvaluateWriteAsync(indexPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("catalog_entry_malformed", decision.DenialReason);
            Assert.Null(decision.LockHandle);

            // Nothing was applied — the on-disk file is untouched.
            Assert.Equal(ExistingCatalog, await File.ReadAllTextAsync(indexPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedNewCatalogLine_MissingLinkBrackets_Denies_CatalogEntryMalformed()
    {
        var root = CreateTempDir();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await File.WriteAllTextAsync(indexPath, ExistingCatalog);

            var guard = NewGuard(Path.Combine(root, "write-locks"), indexPath);
            guard.OnReadFile(indexPath, ExistingCatalog);

            // Starts with the catalog-line marker "- [" but the link markup is broken
            // (no closing ")" after the path) — still in scope for the check since it
            // starts with "- [", and fails the pattern.
            var proposed = ExistingCatalog +
                "- [Retry Backoff](concepts/retry-backoff.md — Beschreibt exponentielles Backoff — 2 Quellen\n";

            var decision = await guard.EvaluateWriteAsync(indexPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.False(decision.IsAllowed);
            Assert.Equal("catalog_entry_malformed", decision.DenialReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EditAddingSectionHeading_WithNoNewCatalogLine_IsNotDenied()
    {
        var root = CreateTempDir();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await File.WriteAllTextAsync(indexPath, ExistingCatalog);

            var guard = NewGuard(Path.Combine(root, "write-locks"), indexPath);
            guard.OnReadFile(indexPath, ExistingCatalog);

            // Adds a new section heading, no new "- [" line at all.
            var proposed = ExistingCatalog + "\n## Patterns\n";

            var decision = await guard.EvaluateWriteAsync(indexPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EditLeavingExistingConformingLineUntouched_WhileAddingWellFormedLine_IsNotDenied()
    {
        var root = CreateTempDir();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            await File.WriteAllTextAsync(indexPath, ExistingCatalog);

            var guard = NewGuard(Path.Combine(root, "write-locks"), indexPath);
            guard.OnReadFile(indexPath, ExistingCatalog);

            // The existing catalog line is carried over byte-for-byte, unrelated
            // surrounding heading text changes, and one new well-formed line is added —
            // only the genuinely new "- [" line is in scope for the check.
            var proposed = "# Wiki Index\n\n## Concepts & Patterns\n\n" +
                "- [Circuit Breaker](concepts/circuit-breaker.md) — Beschreibt Muster gegen Kaskadenausfälle — 3 Quellen\n" +
                "- [Retry Backoff](concepts/retry-backoff.md) — Beschreibt exponentielles Backoff bei Wiederholungen — 2 Quellen\n";

            var decision = await guard.EvaluateWriteAsync(indexPath, WriteMode.ReadWrite, proposed, CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteToOtherTarget_IsNeverFormatChecked_EvenWithIndexPathConfigured()
    {
        var root = CreateTempDir();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            var otherTarget = Path.Combine(root, "wiki", "concepts", "example.md");
            Directory.CreateDirectory(Path.GetDirectoryName(otherTarget)!);

            var guard = NewGuard(Path.Combine(root, "write-locks"), indexPath);

            // Content that would fail every index.md format check, written to a different path.
            var decision = await guard.EvaluateWriteAsync(otherTarget, WriteMode.ReadWrite, "- [broken", CancellationToken.None);

            Assert.True(decision.IsAllowed);
            decision.LockHandle!.Dispose();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// T047: the existing <c>guardrails.format_validate</c> span (introduced by US3 for
    /// <c>log.md</c>) also covers <c>index.md</c>, distinguished only by its
    /// <c>target</c> attribute value — same span shape, no second span type.
    /// </summary>
    [Fact]
    public async Task FormatValidateSpan_EmittedWithIndexTarget_NestedUnderAmbientActivity()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "CatalogEntryFormatEnforcementTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var root = CreateTempDir();
        try
        {
            var wikiDir = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiDir);
            var indexPath = Path.Combine(wikiDir, "index.md");
            await File.WriteAllTextAsync(indexPath, string.Empty);

            var policy = new SafetyPolicy(
                root,
                readPrefixes: [wikiDir + Path.DirectorySeparatorChar],
                writePrefixes: [wikiDir + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, root, taskId: "task-catalog-format-span",
                writeLocksDir: Path.Combine(root, "write-locks"),
                indexPath: indexPath,
                activitySource: TestActivitySource);

            Activity? ambient;
            using (ambient = TestActivitySource.StartActivity("test.ambient"))
            {
                // Seed the write guard's compare-and-swap baseline first (read-before-write)
                // — otherwise the write is denied earlier as write_conflict_stale_read,
                // never reaching the format-validation step under test.
                await executor.ExecuteAsync("read_file", """{"path": "wiki/index.md"}""", turn: 1, CancellationToken.None);

                var result = await executor.ExecuteAsync(
                    "write_file",
                    $$"""{"path": "wiki/index.md", "content": "- [broken"}""",
                    turn: 2,
                    CancellationToken.None);

                Assert.True(result.IsError);
            }

            var thisTrace = activities.Where(a => a.TraceId == ambient!.TraceId).ToList();
            var formatSpan = thisTrace.Single(a => a.OperationName == "guardrails.format_validate");

            Assert.Equal(ambient!.SpanId.ToHexString(), formatSpan.ParentSpanId.ToHexString());
            Assert.Equal(indexPath, GetTag(formatSpan, "path"));
            Assert.Equal("index", GetTag(formatSpan, "target"));
            Assert.Equal("denied", GetTag(formatSpan, "outcome"));
            Assert.Equal("catalog_entry_malformed", GetTag(formatSpan, "reason"));
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
        var dir = Path.Combine(Path.GetTempPath(), $"catalog-entry-format-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
