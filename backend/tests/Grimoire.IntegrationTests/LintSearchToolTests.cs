using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using System.Text.Json;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T022-T026 (026-guarded-tool-surface, US1, ADR-030 R1/R2/R5): behavioral coverage for
/// <c>search_files</c>' dispatch in <see cref="GuardedToolExecutor"/>. Exercises the
/// executor directly (mirrors <c>GuardedToolExecutorCoordinationTests</c>' idiom) with a
/// registry built locally that declares <see cref="ToolRegistry.SearchFilesDefinition"/> —
/// <c>LintToolRegistry.Default</c> does not declare <c>search_files</c> yet (deferred to
/// the eval-recapture layer, see this feature's PR history), but the dispatch logic itself
/// is complete and independently testable against any registry that does declare the tool.
/// </summary>
public class LintSearchToolTests
{
    private static readonly ToolRegistry SearchCapableRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.SearchFilesDefinition,
    ]);

    // ── T022: a match inside a read-denied path is absent, no denial names it (SC-001) ──

    [Fact]
    public async Task Search_MatchInsideReadDeniedPath_IsOmittedSilently_NoDenialRecorded()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            var techDir = Path.Combine(wikiRoot, "tech");
            var secretDir = Path.Combine(wikiRoot, "secret");
            Directory.CreateDirectory(techDir);
            Directory.CreateDirectory(secretDir);
            await File.WriteAllTextAsync(Path.Combine(techDir, "visible.md"), "the target term appears here");
            await File.WriteAllTextAsync(Path.Combine(secretDir, "hidden.md"), "the target term also appears here");

            // Read scope covers only tech/ — secret/ is out of scope entirely.
            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [techDir + Path.DirectorySeparatorChar],
                writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target term" }),
                turn: 1,
                CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("tech/visible.md", result.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", result.Content, StringComparison.Ordinal);
            Assert.Empty(executor.Denials);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T023: traversal and symlink search roots canonicalize before policy evaluation ──

    [Fact]
    public async Task Search_PathEscapingTheWikiRoot_IsDeniedAsTraversal()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            Directory.CreateDirectory(Path.Combine(root, "outside"));
            await File.WriteAllTextAsync(Path.Combine(root, "outside", "secret.md"), "target term");

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target", path = "../outside" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            var denial = Assert.Single(executor.Denials);
            Assert.Equal(ToolRegistry.SearchFiles, denial.Action);
            Assert.Equal("traversal", denial.Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task Search_PathPointingThroughASymlinkOutsideTheWikiRoot_IsDeniedAsTraversal()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var outsideDir = Path.Combine(root, "outside");
            Directory.CreateDirectory(outsideDir);
            await File.WriteAllTextAsync(Path.Combine(outsideDir, "secret.md"), "target term");

            var linkPath = Path.Combine(wikiRoot, "escape-link");
            try
            {
                Directory.CreateSymbolicLink(linkPath, outsideDir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                // Symlink creation can require elevated privileges in some sandboxes; the
                // traversal-via-plain-path case above already covers the policy-evaluation
                // contract, so skip rather than fail the run on an environment limitation.
                return;
            }

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target", path = "escape-link" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            var denial = Assert.Single(executor.Denials);
            Assert.Equal("traversal", denial.Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T024: cap reached → truncation signaled; budget exhausted → incomplete signaled ──

    [Fact]
    public async Task Search_ResultCapReached_ReturnsTruncationMarker_WithExactlyCapMatches()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var lines = Enumerable.Range(1, 10).Select(i => $"target term on line {i}");
            await File.WriteAllLinesAsync(Path.Combine(wikiRoot, "many-matches.md"), lines);

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target term", max_results = 3 }),
                turn: 1,
                CancellationToken.None);

            Assert.False(result.IsError);
            var matchLines = result.Content.Split('\n').Count(l => l.Contains("many-matches.md", StringComparison.Ordinal));
            Assert.Equal(3, matchLines);
            Assert.Contains("[truncated: showing the first 3 matches]", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task Search_TimeBudgetExhausted_ReturnsPartialResults_WithIncompleteMarker_NeverAFailedRun()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            for (var i = 0; i < 20; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(wikiRoot, $"page-{i}.md"), "target term");
            }

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            // TimeSpan.Zero is not a valid Regex match timeout (throws
            // ArgumentOutOfRangeException, itself an ArgumentException, at construction) —
            // one tick (100ns) is the smallest valid positive budget, and real file-open +
            // regex-match overhead across 20 files reliably exceeds it, forcing the
            // timed_out path deterministically without a real multi-second wait (mirrors
            // writeLockBackoffCap's rationale).
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry,
                searchTimeBudget: TimeSpan.FromTicks(1));

            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target term" }),
                turn: 1,
                CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Contains("[incomplete:", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T025: an unsupported/oversized pattern is a recorded denial; the run continues ──

    [Fact]
    public async Task Search_LookaheadPattern_IsDeniedAsUnsupportedSyntax_SubsequentCallStillWorks()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            await File.WriteAllTextAsync(Path.Combine(wikiRoot, "page.md"), "target term");

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var rejected = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "(?=target)term" }),
                turn: 1,
                CancellationToken.None);

            Assert.True(rejected.IsError);
            var denial = Assert.Single(executor.Denials);
            Assert.Equal("unsupported_syntax", denial.Reason);

            // The run continues: a subsequent, valid search still succeeds.
            var succeeded = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target term" }),
                turn: 2,
                CancellationToken.None);

            Assert.False(succeeded.IsError);
            Assert.Contains("page.md", succeeded.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task Search_PatternOverThe1000CharacterBound_IsDeniedAsPatternTooLong()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var oversizedPattern = new string('a', 1001);
            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = oversizedPattern }),
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            var denial = Assert.Single(executor.Denials);
            Assert.Equal("pattern_too_long", denial.Reason);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T026 (ADR-030 R5, Feature-Scoped Invariant): the four defaults, through behavior ──

    [Fact]
    public async Task Search_WithNoMaxResults_DefaultCapIs200_A201stMatchTruncates()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var lines = Enumerable.Range(1, 201).Select(i => $"target term on line {i}");
            await File.WriteAllLinesAsync(Path.Combine(wikiRoot, "many-matches.md"), lines);

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target term" }),
                turn: 1,
                CancellationToken.None);

            var matchLines = result.Content.Split('\n').Count(l => l.Contains("many-matches.md", StringComparison.Ordinal));
            Assert.Equal(200, matchLines);
            Assert.Contains("[truncated:", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    [Fact]
    public async Task Search_MaxResultsAbove1000_ClampsToTheHardCeiling()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var lines = Enumerable.Range(1, 1001).Select(i => $"target term on line {i}");
            await File.WriteAllLinesAsync(Path.Combine(wikiRoot, "many-matches.md"), lines);

            var policy = new SafetyPolicy(wikiRoot, readPrefixes: [wikiRoot + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: SearchCapableRegistry);

            var result = await executor.ExecuteAsync(
                ToolRegistry.SearchFiles,
                JsonSerializer.Serialize(new { pattern = "target term", max_results = 5000 }),
                turn: 1,
                CancellationToken.None);

            var matchLines = result.Content.Split('\n').Count(l => l.Contains("many-matches.md", StringComparison.Ordinal));
            Assert.Equal(1000, matchLines);
            Assert.Contains("[truncated:", result.Content, StringComparison.Ordinal);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── shared setup ─────────────────────────────────────────────────────────────

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-search-tool-{Guid.NewGuid():N}");
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
