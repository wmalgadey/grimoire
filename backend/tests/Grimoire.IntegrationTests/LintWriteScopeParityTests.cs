using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T035/T036/T038/T039 (026-guarded-tool-surface, US2, ADR-031 R1/R2): behavioral coverage
/// for Lint's single, mode-independent write scope. Like <c>LintSearchToolTests</c>, this
/// exercises <see cref="GuardedToolExecutor"/> directly against a locally-constructed
/// policy shaped like policy.json v2 (one <c>read-write</c> rule on the content root, no
/// <c>excludePrefixes</c>). The shipped file has since been flipped to v2 (T009) and
/// <c>LintWriteScopeDenialTests</c> now asserts these same grants against it; this file
/// keeps its local policy because its subject is mode-independence, which is a property of
/// the dispatch path rather than of any one policy file. That independence holds by
/// construction: nothing in
/// <see cref="SafetyPolicy"/> or <see cref="GuardedToolExecutor"/> takes a "mode" parameter
/// at all (ADR-031 R1's Phase 0 structural half, <c>GuardedRetrievalNoModeBranchRuleTests</c>/
/// T003, already guards the coordinators; this file is R1's behavioral half).
/// </summary>
public class LintWriteScopeParityTests
{
    private static readonly ToolRegistry FullScopeRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.ReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
        ToolRegistry.DeleteFileDefinition,
    ]);

    // ── T035: the write decision is identical in a survey run and an execution run ──────

    [Fact]
    public async Task WriteToAnExistingPage_IsAllowedIdentically_UnderASurveyShapedAndAnExecutionShapedExecutor()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(Path.Combine(wikiRoot, "tech"));
            var pagePath = Path.Combine(wikiRoot, "tech", "page.md");
            await File.WriteAllTextAsync(pagePath, "original");

            // ADR-031 R1: one policy instance, shared by both "modes" — no per-mode
            // variant is ever constructed in production, and this test does the same.
            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);

            var surveyExecutor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: FullScopeRegistry);
            var executionExecutor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: FullScopeRegistry);

            var surveyResult = await surveyExecutor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "tech/page.md", content = "updated by survey" }),
                turn: 1, CancellationToken.None);
            var executionResult = await executionExecutor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "tech/page.md", content = "updated by execution" }),
                turn: 1, CancellationToken.None);

            Assert.False(surveyResult.IsError);
            Assert.False(executionResult.IsError);
            Assert.Single(surveyExecutor.TouchedPaths);
            Assert.Single(executionExecutor.TouchedPaths);
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T036: writes outside the wiki content root are denied and recorded in both modes ──

    [Theory]
    [InlineData("../outside/secret.md")]
    public async Task WriteOutsideTheContentRoot_IsDeniedIdentically_InBothModes(string escapingPath)
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            Directory.CreateDirectory(Path.Combine(root, "outside"));

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);

            var surveyExecutor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: FullScopeRegistry);
            var executionExecutor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: FullScopeRegistry);

            var surveyResult = await surveyExecutor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = escapingPath, content = "exfiltrated" }),
                turn: 1, CancellationToken.None);
            var executionResult = await executionExecutor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = escapingPath, content = "exfiltrated" }),
                turn: 1, CancellationToken.None);

            Assert.True(surveyResult.IsError);
            Assert.True(executionResult.IsError);
            Assert.Equal("traversal", Assert.Single(surveyExecutor.Denials).Reason);
            Assert.Equal("traversal", Assert.Single(executionExecutor.Denials).Reason);
            Assert.False(File.Exists(Path.Combine(root, "outside", "secret.md")));
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T039, reclassified by 028-lint-at-scale (US3, Clarifications 2026-08-27, FSI-3):
    // index.md stays held to ADR-017's catalog-entry format check under Lint's read-write
    // mode, exactly as under any other agent's; log.md's own format/ordering check no
    // longer denies at all — a non-conforming write commits, and the deviation is
    // observed, never gated — the same for Lint as for every other agent (research.md R15).

    [Fact]
    public async Task WriteToLogMd_NotAppendOnly_Commits_AndReportsDeviation_EvenUnderReadWriteMode()
    {
        var root = CreateTempRoot();
        try
        {
            var wikiRoot = Path.Combine(root, "wiki");
            Directory.CreateDirectory(wikiRoot);
            var logPath = Path.Combine(wikiRoot, "log.md");
            await File.WriteAllTextAsync(logPath, "## [2026-08-01] Existing entry | Ingest\n\nSomething happened.\n");

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writeRules: [new WriteRule(wikiRoot + Path.DirectorySeparatorChar, WriteMode.ReadWrite)]);

            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), wikiRoot,
                registry: FullScopeRegistry,
                writeLocksDir: Path.Combine(root, "write-locks"),
                logPath: logPath);

            // Establish the compare-and-swap read baseline first (ADR-015) — otherwise
            // the write is denied write_conflict_stale_read before the write-lock guard
            // ever reaches the log-format check this test means to exercise.
            await executor.ExecuteAsync(
                ToolRegistry.ReadFile,
                JsonSerializer.Serialize(new { path = "log.md" }),
                turn: 1, CancellationToken.None);

            // Rewrites the file instead of prepending to it — violates the ordering
            // invariant regardless of write mode, but still commits (FSI-3).
            var replacementContent = "## [2026-08-02] Replaced everything | Lint\n\nGone.\n";
            var result = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                JsonSerializer.Serialize(new { path = "log.md", content = replacementContent }),
                turn: 2, CancellationToken.None);

            Assert.False(result.IsError);
            Assert.Empty(executor.Denials);
            Assert.Equal(replacementContent, await File.ReadAllTextAsync(logPath));
        }
        finally
        {
            CleanUp(root);
        }
    }

    // ── T038: an authorized remediation targeting page content runs under a scope that
    // permits it — the real RemediationRunCoordinator passes the same PolicyPath a Lint
    // survey run would (ADR-031 R1/FR-016, SC-006), regardless of the recorded target ──

    [Fact]
    public async Task AuthorizedRemediation_TargetingPageContent_IsDispatchedUnderTheSameLintPolicyPath()
    {
        await using var app = await StartHubHostAsync();
        await using var harness = await RemediationCoordinatorHarness.CreateAsync(app, autoPlay: false);

        var proposedAt = DateTimeOffset.UtcNow;
        await harness.Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: "2026-08-22-remediation-body-edit",
            RunId: "2026-08-22-lint-run",
            Title: "Fix stale content",
            Description: "The page body is out of date.",
            TargetPath: "tech/page.md",
            State: RemediationTaskStates.Authorized,
            ProposedAt: proposedAt,
            AuthorizedAt: proposedAt,
            OutcomeReason: null,
            UpdatedAt: proposedAt));
        await harness.RecordStore.CreateAsync(
            "2026-08-22-remediation-body-edit", "2026-08-22-lint-run", proposedAt,
            "Fix stale content", "The page body is out of date.", "tech/page.md");

        await harness.Coordinator.TryStartNextAsync();

        var request = Assert.Single(harness.Launcher.RemediationRequests);
        Assert.Equal("tech/page.md", request.TargetPath);
        // FR-016: the recorded target is a hint, never a scope boundary — the dispatched
        // request runs under exactly the same policy path a whole-wiki survey run does.
        Assert.Equal(harness.Paths.Lint.PolicyPath, request.PolicyPath);
    }

    // ── shared setup ─────────────────────────────────────────────────────────────
    // (mirrors RemediationRunCoordinatorTests.StartHubHostAsync's idiom)

    private static async Task<WebApplication> StartHubHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        var app = builder.Build();
        app.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
        await app.StartAsync();
        return app;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-write-parity-{Guid.NewGuid():N}");
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
