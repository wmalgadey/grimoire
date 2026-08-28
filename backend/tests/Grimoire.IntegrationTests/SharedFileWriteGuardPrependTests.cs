using System.Text.Json;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent;
using Grimoire.LintAgent;
using Grimoire.QueryAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// 028-lint-at-scale, US3 (FR-010, FR-011, FR-012, FR-016, FSI-1/FSI-2/FSI-3,
/// contracts/log-prepend-write.md) — <c>write_file</c>'s <c>mode: "prepend"</c> call
/// shape: schema addition, no-baseline dispatch, concurrency safety, and the
/// deny-to-monitor reclassification of the activity-log format check on both call
/// shapes. Classicist, state-based assertions over the real
/// <see cref="GuardedToolExecutor"/>/<see cref="Coordination.SharedFileWriteGuard"/>
/// stack — no reflection, no mocking.
/// </summary>
[Collection("HubActivityListenerObservability")]
public class SharedFileWriteGuardPrependTests
{
    private const string ConformingEntry =
        "## [2026-08-25] ingest | created retrieval-patterns\n\nCreated [[concepts/retrieval-patterns]] from source \"source.md\". Task: task-001.\n";

    // ── T017: schema addition (FSI-1) ───────────────────────────────────────────────────

    [Fact]
    public void WriteFileDefinition_SchemaGainsOptionalModeEnum_KeepsAdditionalPropertiesFalse_AndUnchangedRequired()
    {
        using var schema = JsonDocument.Parse(ToolRegistry.WriteFileDefinition.InputSchemaJson);
        var root = schema.RootElement;

        Assert.False(root.GetProperty("additionalProperties").GetBoolean());

        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["path", "content"], required);

        var mode = root.GetProperty("properties").GetProperty("mode");
        Assert.Equal("string", mode.GetProperty("type").GetString());
        var enumValues = mode.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["replace", "prepend"], enumValues);
    }

    /// <summary>
    /// The same static <see cref="ToolDefinition"/> instance is declared by all three
    /// registries (<see cref="AgentProfileFidelityTests"/> already proves each registry's
    /// tool list includes it) — the schema addition and the prepend dispatch path below
    /// are therefore identical for Lint, Ingest, and Query by construction, not by
    /// separate implementations that could drift.
    /// </summary>
    [Fact]
    public void WriteFileDefinition_IsTheSameSharedInstance_AcrossAllThreeRegistries()
    {
        Assert.Same(ToolRegistry.WriteFileDefinition, IngestToolRegistry.Default.Tools.Single(t => t.Name == ToolRegistry.WriteFile));
        Assert.Same(ToolRegistry.WriteFileDefinition, QueryToolRegistry.Default.Tools.Single(t => t.Name == ToolRegistry.WriteFile));
        Assert.Same(ToolRegistry.WriteFileDefinition, LintToolRegistry.Default.Tools.Single(t => t.Name == ToolRegistry.WriteFile));
    }

    [Fact]
    public async Task WriteFileCall_ModeOmitted_IsByteIdenticalToTodaysBehavior()
    {
        var (executor, wikiRoot) = BuildExecutor();
        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", content = ConformingEntry }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(ConformingEntry, await File.ReadAllTextAsync(Path.Combine(wikiRoot, "log.md")));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── T018: no-baseline prepend dispatch (FSI-2) ──────────────────────────────────────

    [Fact]
    public async Task PrependWrite_WithNoPrecedingReadFile_Succeeds_AndCommitsEntryPlusCurrentContentByteForByte()
    {
        var (executor, wikiRoot) = BuildExecutor();
        var logPath = Path.Combine(wikiRoot, "log.md");
        var existing = "## [2026-08-01] ingest | Prior entry\n\nPrior paragraph.\n";
        await File.WriteAllTextAsync(logPath, existing);

        try
        {
            // No read_file call at all — the no-baseline dispatch path this Feature-Scoped
            // Invariant requires (research.md R8: there is no staleness scenario for a
            // prepend to be stale against).
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", mode = "prepend", content = ConformingEntry }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Empty(executor.Denials);

            var committed = await File.ReadAllTextAsync(logPath);
            Assert.Equal(ConformingEntry + existing, committed);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task PrependWrite_ToAMissingLog_TreatsItAsAnEmptyBase_TheEntryBecomesTheWholeFile()
    {
        var (executor, wikiRoot) = BuildExecutor();
        var logPath = Path.Combine(wikiRoot, "log.md");

        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", mode = "prepend", content = ConformingEntry }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(ConformingEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── T019: concurrent writers, no lost entries (FSI-2, FR-012, SC-008) ───────────────

    [Fact]
    public async Task TwoConcurrentPrependWriters_BothLand_NewestFirst_InLockAcquisitionOrder_NoLostEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"prepend-concurrency-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);
        var writeLocksDir = Path.Combine(root, "write-locks");
        var logPath = Path.Combine(wikiRoot, "log.md");
        var existing = "## [2026-08-01] ingest | Prior entry\n\nPrior paragraph.\n";
        await File.WriteAllTextAsync(logPath, existing);

        try
        {
            // Two separate GuardedToolExecutor instances sharing the same writeLocksDir/
            // logPath — exactly as two concurrent OS agent processes would (ADR-015).
            var writerA = BuildExecutorSharing(wikiRoot, writeLocksDir);
            var writerB = BuildExecutorSharing(wikiRoot, writeLocksDir);

            var entryA = "## [2026-08-25] ingest | entry-A\n\nFrom writer A.\n";
            var entryB = "## [2026-08-25] query | entry-B\n\nFrom writer B.\n";

            // Both launched before either is awaited, so the two lock-acquisition attempts
            // genuinely race (contract "Concurrent writers": serialized by the lock, not
            // denied — whichever order they actually acquire it in).
            var taskA = writerA.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", mode = "prepend", content = entryA }),
                turn: 1, CancellationToken.None);
            var taskB = writerB.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", mode = "prepend", content = entryB }),
                turn: 1, CancellationToken.None);

            var results = await Task.WhenAll(taskA, taskB);

            Assert.False(results[0].IsError);
            Assert.False(results[1].IsError);
            Assert.Empty(writerA.Denials);
            Assert.Empty(writerB.Denials);

            var final = await File.ReadAllTextAsync(logPath);
            Assert.Contains(entryA, final, StringComparison.Ordinal);
            Assert.Contains(entryB, final, StringComparison.Ordinal);
            Assert.Contains(existing, final, StringComparison.Ordinal);

            // Every entry lands, newest-first relative to the existing content, in
            // whichever order the two writers actually acquired the lock — both possible
            // orderings are correct (contract "Concurrent writers").
            Assert.True(
                final == entryA + entryB + existing || final == entryB + entryA + existing,
                $"Expected both entries above the prior content in lock-acquisition order. Actual:\n{final}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── T020/T021: never denied, deviation signal (FSI-3, FR-011, FR-016, SC-009) ───────

    [Theory]
    [InlineData("prepend")]
    [InlineData("replace")]
    public async Task MalformedHeadingWrite_CommitsExactlyAsSubmitted_OnBothModes(string mode)
    {
        var (executor, wikiRoot) = BuildExecutor();
        var logPath = Path.Combine(wikiRoot, "log.md");
        var existing = "## [2026-08-01] ingest | Prior entry\n\nPrior paragraph.\n";
        await File.WriteAllTextAsync(logPath, existing);

        try
        {
            var malformedEntry = "Not a heading at all\n\nSome text.\n";
            var content = mode == "prepend" ? malformedEntry : malformedEntry + existing;

            if (mode == "replace")
            {
                await executor.ExecuteAsync(ToolRegistry.ReadFile, """{"path": "log.md"}""", turn: 1, CancellationToken.None);
            }

            var call = new Dictionary<string, object?> { ["path"] = "log.md", ["content"] = content };
            if (mode == "prepend")
            {
                call["mode"] = mode;
            }

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(call),
                turn: 2, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Empty(executor.Denials);
            Assert.Equal(mode == "prepend" ? malformedEntry + existing : content, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task PrependWrite_WithWrongOrder_IsImpossibleByConstruction_TheHarnessAlwaysAssemblesEntryFirst()
    {
        // Unlike mode: "replace" (where the agent proposes the whole file and can get the
        // order wrong), mode: "prepend" always assembles entry + currentContent itself
        // (data-model.md) — there is no ordering deviation to construct for this mode.
        // The replace-mode ordering deviation is covered by
        // LogEntryFormatEnforcementTests.OldAppendShape_Commits_AndReportsLogEntryNotPrepended.
        var (executor, wikiRoot) = BuildExecutor();
        var logPath = Path.Combine(wikiRoot, "log.md");
        var existing = "## [2026-08-01] ingest | Prior entry\n\nPrior paragraph.\n";
        await File.WriteAllTextAsync(logPath, existing);

        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", mode = "prepend", content = ConformingEntry }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(ConformingEntry + existing, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task ConformingPrependWrite_EmitsNoDeviation()
    {
        var (executor, wikiRoot) = BuildExecutor();
        var logPath = Path.Combine(wikiRoot, "log.md");
        await File.WriteAllTextAsync(logPath, string.Empty);

        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", mode = "prepend", content = ConformingEntry }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Empty(executor.Denials);
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── T022: cost proportional to entry, not file (SC-007) ─────────────────────────────

    [Fact]
    public async Task PrependWrite_AgainstA128KbSeededLog_Succeeds_AndTheCallsOwnContentIsTheEntryLengthOnly()
    {
        var (executor, wikiRoot) = BuildExecutor();
        var logPath = Path.Combine(wikiRoot, "log.md");

        // Seed ~128KB to reproduce the production size that triggered issue #201.
        var seededEntry = "## [2026-01-01] ingest | seeded\n\n" + new string('x', 130_000) + "\n";
        await File.WriteAllTextAsync(logPath, seededEntry);
        Assert.True(new FileInfo(logPath).Length > 128_000);

        try
        {
            var callContent = JsonSerializer.Serialize(new { path = "log.md", mode = "prepend", content = ConformingEntry });

            // The call's own JSON payload — what an agent actually has to produce/transmit
            // — never includes the seeded file's bytes at all (SC-007's cost claim).
            Assert.True(callContent.Length < seededEntry.Length);
            Assert.DoesNotContain("seeded", callContent, StringComparison.Ordinal);

            var result = await executor.ExecuteAsync(ToolRegistry.WriteFile, callContent, turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(ConformingEntry + seededEntry, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── index.md is unaffected (contract "What does not change") ───────────────────────

    /// <summary>
    /// Prepend is a generic concatenation mechanism, accepted for any target path
    /// (contract: "accepted for any target path, not only log.md") — but index.md's own
    /// catalog-entry check is gated on the <c>mode: "replace"</c> dispatch path
    /// specifically and is "not reachable from any prepend-mode code path" (contract
    /// "What does not change"). A prepend-mode write to index.md therefore succeeds
    /// unconditionally, with no catalog validation at all — <see cref="ValidateIndexEntryDenies_OnlyOnTheReplacePath"/>
    /// below confirms the same content, sent as a <c>mode: "replace"</c> write, is denied.
    /// </summary>
    [Fact]
    public async Task PrependMode_AgainstIndexMd_Succeeds_WithNoCatalogCheckAtAll()
    {
        var (executor, wikiRoot) = BuildExecutor();
        var indexPath = Path.Combine(wikiRoot, "index.md");
        const string existing = "# Wiki Index\n\n## Concepts\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — desc — 3 sources\n";
        await File.WriteAllTextAsync(indexPath, existing);

        try
        {
            var offendingLine = "- [Retry Backoff](concepts/retry-backoff.md) missing the separators\n";
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "index.md", mode = "prepend", content = offendingLine }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(offendingLine + existing, await File.ReadAllTextAsync(indexPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task ValidateIndexEntryDenies_OnlyOnTheReplacePath()
    {
        var (executor, wikiRoot) = BuildExecutor();
        var indexPath = Path.Combine(wikiRoot, "index.md");
        const string existing = "# Wiki Index\n\n## Concepts\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — desc — 3 sources\n";
        await File.WriteAllTextAsync(indexPath, existing);

        try
        {
            await executor.ExecuteAsync(ToolRegistry.ReadFile, """{"path": "index.md"}""", turn: 1, CancellationToken.None);

            var offendingLine = "- [Retry Backoff](concepts/retry-backoff.md) missing the separators\n";
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "index.md", content = existing + offendingLine }),
                turn: 2, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains("catalog_entry_malformed", result.Content, StringComparison.Ordinal);
            Assert.Equal(existing, await File.ReadAllTextAsync(indexPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    // ── CreateOnly/FrontmatterOnly scope still applies to prepend (Copilot review, PR #208) ──
    //
    // Prepend is a different call SHAPE, not a different authority: a scope's WriteMode
    // (data-model.md "Two distinct 'mode' concepts") must gate a prepend write exactly as it
    // gates a replace write, or a CreateOnly/FrontmatterOnly-scoped agent (e.g. Query's
    // create-only content scope, policy.json) could use mode: "prepend" to bypass its own
    // scope restriction entirely.

    [Fact]
    public async Task CreateOnlyScope_PrependAgainstAnExistingTarget_IsDenied()
    {
        var (executor, wikiRoot) = BuildExecutorWithWriteMode(WriteMode.CreateOnly);
        var targetPath = Path.Combine(wikiRoot, "pages", "existing.md");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        const string existing = "# Existing page\n\nAlready here.\n";
        await File.WriteAllTextAsync(targetPath, existing);

        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "pages/existing.md", mode = "prepend", content = "New content.\n" }),
                turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Single(executor.Denials);
            Assert.Equal("create_only_target_exists", executor.Denials[0].Reason);
            Assert.Equal(existing, await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task CreateOnlyScope_PrependAgainstANewTarget_IsAllowed_MatchingReplaceModesOwnCreateBehavior()
    {
        var (executor, wikiRoot) = BuildExecutorWithWriteMode(WriteMode.CreateOnly);
        var targetPath = Path.Combine(wikiRoot, "pages", "brand-new.md");

        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "pages/brand-new.md", mode = "prepend", content = "Whole file.\n" }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Empty(executor.Denials);
            Assert.Equal("Whole file.\n", await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyScope_PrependAgainstAnExistingTarget_IsDenied()
    {
        var (executor, wikiRoot) = BuildExecutorWithWriteMode(WriteMode.FrontmatterOnly);
        var targetPath = Path.Combine(wikiRoot, "pages", "existing.md");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        const string existing = "---\ntitle: Existing\n---\n\nBody paragraph.\n";
        await File.WriteAllTextAsync(targetPath, existing);

        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "pages/existing.md", mode = "prepend", content = "Injected body text.\n" }),
                turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Single(executor.Denials);
            // Prepending puts the new entry before the "---" frontmatter delimiter, so the
            // assembled content no longer parses as frontmatter at all — a stronger, more
            // specific denial than "frontmatter_only_body_changed" for the same underlying
            // violation (a body-content change under a frontmatter-only scope).
            Assert.Equal("frontmatter_only_malformed_document", executor.Denials[0].Reason);
            Assert.Equal(existing, await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyScope_PrependAgainstAMissingTarget_IsDenied()
    {
        var (executor, wikiRoot) = BuildExecutorWithWriteMode(WriteMode.FrontmatterOnly);
        var targetPath = Path.Combine(wikiRoot, "pages", "missing.md");

        try
        {
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "pages/missing.md", mode = "prepend", content = "New body.\n" }),
                turn: 1, CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Single(executor.Denials);
            Assert.Equal("frontmatter_only_target_missing", executor.Denials[0].Reason);
            Assert.False(File.Exists(targetPath));
        }
        finally
        {
            CleanUp(wikiRoot);
        }
    }

    private static (GuardedToolExecutor Executor, string WikiRoot) BuildExecutor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"prepend-write-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);

        var executor = BuildExecutorSharing(wikiRoot, Path.Combine(root, "write-locks"));
        return (executor, wikiRoot);
    }

    private static (GuardedToolExecutor Executor, string WikiRoot) BuildExecutorWithWriteMode(WriteMode mode)
    {
        var root = Path.Combine(Path.GetTempPath(), $"prepend-write-mode-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);

        var policy = new SafetyPolicy(
            wikiRoot,
            readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
            writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, mode)]);

        var executor = new GuardedToolExecutor(
            policy, new WriteJournal(), wikiRoot,
            writeLocksDir: Path.Combine(root, "write-locks"),
            logPath: Path.Combine(wikiRoot, "log.md"),
            indexPath: Path.Combine(wikiRoot, "index.md"));

        return (executor, wikiRoot);
    }

    private static GuardedToolExecutor BuildExecutorSharing(string wikiRoot, string writeLocksDir)
    {
        var policy = new SafetyPolicy(
            wikiRoot,
            readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
            writePrefixes: [wikiRoot + Path.DirectorySeparatorChar]);

        return new GuardedToolExecutor(
            policy, new WriteJournal(), wikiRoot,
            writeLocksDir: writeLocksDir,
            logPath: Path.Combine(wikiRoot, "log.md"),
            indexPath: Path.Combine(wikiRoot, "index.md"));
    }

    private static void CleanUp(string wikiRoot)
    {
        var root = Path.GetDirectoryName(wikiRoot)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
