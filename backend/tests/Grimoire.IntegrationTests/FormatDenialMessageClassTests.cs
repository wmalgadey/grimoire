using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;

namespace Grimoire.IntegrationTests;

/// <summary>
/// Issue #182: the tool-result message <see cref="GuardedToolExecutor"/> returns for a
/// denied <c>write_file</c> call MUST classify the denial correctly. A format rejection
/// (ADR-017's <c>catalog_entry_malformed</c>) is a fully in-scope write whose proposed
/// content had the wrong shape — the message must say so and tell the agent to reissue,
/// never "outside the safety policy; continue with your remaining allowed work" (that
/// phrase describes an unfixable policy-scope rejection and, for a format denial, told a
/// real production run to abandon a page that was never one edit away from being
/// accepted). A genuine policy-scope denial (<c>out_of_scope</c>) keeps the original
/// phrase unchanged. Deterministic and hermetic: this is the harness's own text, not
/// agent judgment (Constitution Principle V).
///
/// 028-lint-at-scale (US3, Clarifications 2026-08-27, FSI-3): the three <c>log_entry_*</c>
/// reasons this class used to cover here no longer deny at all — a non-conforming log.md
/// write now commits, so there is no denial message left to classify for them. Their
/// former test cases are replaced below with commit-and-report assertions; the message
/// text and reason-code taxonomy they exercise now live in the
/// <c>wiki.log.format_deviation</c> signal instead (<see cref="LogEntryFormatEnforcementTests"/>).
/// </summary>
public class FormatDenialMessageClassTests
{
    private const string GiveUpPhrase = "outside the safety policy; continue with your remaining allowed work";

    [Fact]
    public async Task CatalogEntryMalformed_MessageSaysReissue_NotGiveUp_AndNamesOffendingLine()
    {
        var root = CreateTempRoot();
        try
        {
            var indexPath = Path.Combine(root, "wiki", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            var existing = "# Wiki Index\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — desc — 3 sources\n";
            await File.WriteAllTextAsync(indexPath, existing);

            var executor = BuildExecutor(root, indexPath: indexPath, logPath: null);
            await executor.ExecuteAsync(ToolRegistry.ReadFile, $$"""{"path": "wiki/index.md"}""", turn: 1, CancellationToken.None);

            var offendingLine = "- [Retry Backoff](concepts/retry-backoff.md) missing the separators";
            var proposed = existing + offendingLine + "\n";

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                $$"""{"path": "wiki/index.md", "content": {{System.Text.Json.JsonSerializer.Serialize(proposed)}}}""",
                turn: 2,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains("catalog_entry_malformed", result.Content, StringComparison.Ordinal);
            Assert.DoesNotContain(GiveUpPhrase, result.Content, StringComparison.Ordinal);
            Assert.Contains("reissue this write", result.Content, StringComparison.Ordinal);
            Assert.Contains(offendingLine, result.Content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 028-lint-at-scale (US3, Clarifications 2026-08-27, FSI-3): replaces
    /// <c>LogEntryNotPrepended_MessageSaysReissue_NotGiveUp</c> — the same non-prepended
    /// shape now commits rather than being denied; there is no message to classify.
    /// </summary>
    [Fact]
    public async Task LogEntryNotPrepended_Commits_WithNoDenialMessage()
    {
        var root = CreateTempRoot();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var existing = "## [2026-08-01] ingest | Prior entry\n\nPrior paragraph.\n";
            await File.WriteAllTextAsync(logPath, existing);

            var executor = BuildExecutor(root, indexPath: null, logPath: logPath);
            await executor.ExecuteAsync(ToolRegistry.ReadFile, $$"""{"path": "wiki/log.md"}""", turn: 1, CancellationToken.None);

            // Does not end with the existing content at all — not prepended.
            var proposed = "## [2026-08-23] ingest | New entry\n\nNew paragraph.\n";

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                $$"""{"path": "wiki/log.md", "content": {{System.Text.Json.JsonSerializer.Serialize(proposed)}}}""",
                turn: 2,
                CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(proposed, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 028-lint-at-scale (US3, Clarifications 2026-08-27, FSI-3): replaces
    /// <c>LogEntryMalformedHeading_MessageSaysReissue_NotGiveUp</c> — a malformed heading
    /// now commits rather than being denied; there is no message to classify.
    /// </summary>
    [Fact]
    public async Task LogEntryMalformedHeading_Commits_WithNoDenialMessage()
    {
        var root = CreateTempRoot();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var existing = "## [2026-08-01] ingest | Prior entry\n\nPrior paragraph.\n";
            await File.WriteAllTextAsync(logPath, existing);

            var executor = BuildExecutor(root, indexPath: null, logPath: logPath);
            await executor.ExecuteAsync(ToolRegistry.ReadFile, $$"""{"path": "wiki/log.md"}""", turn: 1, CancellationToken.None);

            var proposed = "Not a heading at all\n\n" + existing;

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                $$"""{"path": "wiki/log.md", "content": {{System.Text.Json.JsonSerializer.Serialize(proposed)}}}""",
                turn: 2,
                CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(proposed, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 028-lint-at-scale (US3, Clarifications 2026-08-27, FSI-3): replaces
    /// <c>LogEntryMissingParagraph_MessageSaysReissue_NotGiveUp</c> — a heading with no
    /// following paragraph now commits rather than being denied; there is no message to
    /// classify.
    /// </summary>
    [Fact]
    public async Task LogEntryMissingParagraph_Commits_WithNoDenialMessage()
    {
        var root = CreateTempRoot();
        try
        {
            var logPath = Path.Combine(root, "wiki", "log.md");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var existing = "## [2026-08-01] ingest | Prior entry\n\nPrior paragraph.\n";
            await File.WriteAllTextAsync(logPath, existing);

            var executor = BuildExecutor(root, indexPath: null, logPath: logPath);
            await executor.ExecuteAsync(ToolRegistry.ReadFile, $$"""{"path": "wiki/log.md"}""", turn: 1, CancellationToken.None);

            var proposed = "## [2026-08-23] ingest | New entry\n" + existing;

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                $$"""{"path": "wiki/log.md", "content": {{System.Text.Json.JsonSerializer.Serialize(proposed)}}}""",
                turn: 2,
                CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Equal(proposed, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PolicyScopeDenial_OutOfScope_KeepsOriginalGiveUpMessage()
    {
        var root = CreateTempRoot();
        try
        {
            var executor = BuildExecutor(root, indexPath: null, logPath: null);

            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                $$"""{"path": "outside/scope.md", "content": "anything"}""",
                turn: 1,
                CancellationToken.None);

            Assert.True(result.IsError);
            Assert.Contains("out_of_scope", result.Content, StringComparison.Ordinal);
            Assert.Contains(GiveUpPhrase, result.Content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GuardedToolExecutor BuildExecutor(string root, string? indexPath, string? logPath)
    {
        var wikiPrefix = Path.Combine(root, "wiki") + Path.DirectorySeparatorChar;

        var policy = new SafetyPolicy(
            root,
            readPrefixes: [wikiPrefix],
            writePrefixes: [wikiPrefix]);

        var journal = new WriteJournal();
        return new GuardedToolExecutor(
            policy,
            journal,
            root,
            taskId: "task-format-denial-message",
            writeLocksDir: Path.Combine(root, "write-locks"),
            logPath: logPath,
            indexPath: indexPath);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"format-denial-message-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
