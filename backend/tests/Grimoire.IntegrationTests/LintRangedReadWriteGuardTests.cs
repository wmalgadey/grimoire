using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using System.Text.Json;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T050-T052 (026-guarded-tool-surface, US3, ADR-030 R3): behavioral coverage for
/// <c>read_file</c>'s ranged/frontmatter-only shapes in <see cref="GuardedToolExecutor"/>.
/// Mirrors <c>LintSearchToolTests</c>' idiom: exercises the executor directly with a
/// registry that declares <see cref="ToolRegistry.RangedReadFileDefinition"/> —
/// <c>LintToolRegistry.Default</c> still declares the unchanged
/// <see cref="ToolRegistry.ReadFileDefinition"/> until the eval-recapture layer, but the
/// dispatch logic (parsing <c>offset</c>/<c>limit</c>/<c>frontmatter_only</c> off the
/// input JSON regardless of which schema advertised the call) is complete now.
/// </summary>
public class LintRangedReadWriteGuardTests
{
    private static readonly ToolRegistry RangedReadRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.RangedReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
    ]);

    // ── T050: a page read only in part cannot license an overwrite (ADR-015/FR-010) ────

    [Fact]
    public async Task WriteAfterAPartialRead_IsRejected_AsStaleRead_AndRecorded()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var pagePath = Path.Combine(wikiRoot, "page.md");
            await File.WriteAllTextAsync(pagePath, "line one\nline two\nline three\n");

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiRoot,
                registry: RangedReadRegistry,
                writeLocksDir: Path.Combine(root, "write-locks"));

            // Partial read: only line 1, never the whole page.
            var readResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "page.md", offset = 1, limit = 1 }),
                turn: 1, CancellationToken.None);
            Assert.False(readResult.IsError);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "page.md", content = "overwritten" }),
                turn: 2, CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Equal("write_conflict_stale_read", Assert.Single(executor.Denials).Reason);
            Assert.Equal("line one\nline two\nline three\n", await File.ReadAllTextAsync(pagePath));
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task WriteAfterAFrontmatterOnlyRead_IsRejected_AsStaleRead()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var pagePath = Path.Combine(wikiRoot, "page.md");
            await File.WriteAllTextAsync(pagePath, "---\ntitle: Page\n---\nBody.\n");

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiRoot,
                registry: RangedReadRegistry,
                writeLocksDir: Path.Combine(root, "write-locks"));

            await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "page.md", frontmatter_only = true }),
                turn: 1, CancellationToken.None);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "page.md", content = "---\ntitle: Renamed\n---\nBody.\n" }),
                turn: 2, CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Equal("write_conflict_stale_read", Assert.Single(executor.Denials).Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T051: no range parameters at all → byte-for-byte whole-file read, baseline set ──

    [Fact]
    public async Task ReadWithNoRangeParameters_ReturnsWholeFileByteForByte_AndSetsBaseline()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var pagePath = Path.Combine(wikiRoot, "page.md");
            const string original = "line one\nline two\nline three\n";
            await File.WriteAllTextAsync(pagePath, original);

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiRoot,
                registry: RangedReadRegistry,
                writeLocksDir: Path.Combine(root, "write-locks"));

            var readResult = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "page.md" }),
                turn: 1, CancellationToken.None);
            Assert.Equal(original, readResult.Content);

            // The whole file was read this run, so an unconditional overwrite is allowed —
            // proof the compare-and-swap baseline was set (unlike the partial-read tests).
            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "page.md", content = "replaced" }),
                turn: 2, CancellationToken.None);

            Assert.False(writeResult.IsError);
            Assert.Empty(executor.Denials);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T052: a range beyond end-of-file is a signal, never a failed run ───────────────

    [Fact]
    public async Task ReadRangeBeyondEndOfFile_ReturnsExplicitEofSignal_NeverAnError()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            await File.WriteAllTextAsync(Path.Combine(wikiRoot, "page.md"), "line one\nline two\n");

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: RangedReadRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "page.md", offset = 100, limit = 5 }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("[end of file: 2 line(s) total]", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task ReadRangeExtendingPastEndOfFile_ReturnsPartialResult_WithEofSignal()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            await File.WriteAllTextAsync(Path.Combine(wikiRoot, "page.md"), "line one\nline two\nline three\n");

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: RangedReadRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "page.md", offset = 2, limit = 10 }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("line two\nline three", result.Content, StringComparison.Ordinal);
            Assert.Contains("[end of file: 3 line(s) total]", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task FrontmatterOnlyRead_ReturnsJustTheFrontmatterBlock()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            await File.WriteAllTextAsync(
                Path.Combine(wikiRoot, "page.md"),
                "---\ntitle: Page\nstatus: active\n---\nThe body, which must not appear.\n");

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: RangedReadRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "page.md", frontmatter_only = true }),
                turn: 1, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("title: Page", result.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("body, which must not appear", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── shared setup ─────────────────────────────────────────────────────────────

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-ranged-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CleanUp(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
