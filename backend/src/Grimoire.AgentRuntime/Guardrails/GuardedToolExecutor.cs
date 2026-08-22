using Grimoire.Domain.Guardrails;
using Grimoire.AgentRuntime.Guardrails.Coordination;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grimoire.AgentRuntime.Guardrails;

/// <summary>
/// Result of executing one guarded tool call.
/// </summary>
public sealed record ToolExecutionResult(
    bool IsError,
    string Content);

/// <summary>
/// Mediates every tool call from the agent loop: canonicalize target → evaluate
/// safety policy → deny (record, emit telemetry, return is_error) or allow
/// (journal for writes, execute, record touched path, emit telemetry).
/// Contracts per <c>contracts/guarded-tools.md</c>.
/// </summary>
public sealed class GuardedToolExecutor
{
    // T040 (012-query-synthesis-writes, US3): plain `Encoding.UTF8` writes a byte-order-mark
    // preamble; `SharedFileWriteGuard`'s compare-and-swap hashes the raw on-disk bytes
    // (including any BOM) against a hash computed from the decoded string content (which
    // never includes one) — a BOM/no-BOM mismatch that spuriously denied every second
    // guarded write to the same target as `write_conflict_stale_read`, discovered by
    // ConcurrentWikiWriteIntegrityTests' multi-writer contention scenario (T036/T039).
    // Wiki markdown files carry no BOM by convention either way.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SafetyPolicy _policy;
    private readonly WriteJournal _journal;
    private readonly string _repositoryRoot;
    private readonly string _taskId;
    private readonly ToolRegistry _registry;
    private readonly IToolCallInstrumentation _instrumentation;
    private readonly SharedFileWriteGuard? _writeGuard;
    private readonly string? _canonicalLogPath;

    // T002 RED PROBE (026-guarded-tool-surface): a plain, non-bounded Regex construction —
    // to be reverted immediately after CI confirms SearchRegexBoundaryRuleTests fails
    // naming this violation.
    private static readonly Regex RedProbeRegex = new(".*");
    private readonly List<DeniedActionRecord> _denials = [];
    private readonly List<string> _touchedPaths = [];
    private readonly List<string> _createdPaths = [];

    /// <param name="writeLocksDir">
    /// ADR-015: base directory for cross-process write-coordination lock files. When
    /// supplied, every guarded write is additionally coordinated through a
    /// <see cref="SharedFileWriteGuard"/> (create-only existence check /
    /// read-then-write compare-and-swap, ADR-015). When <c>null</c> (the default), no
    /// coordination guard is constructed and writes behave exactly as before this
    /// feature — existing callers that do not yet supply this argument are unaffected.
    /// </param>
    /// <param name="writeLockBackoffCap">
    /// T043 (012-query-synthesis-writes, US3): overrides <see cref="SharedFileWriteGuard"/>'s
    /// lock-acquisition backoff cap (default <see cref="CrossProcessFileLock.DefaultBackoffCap"/>,
    /// 5 seconds). Exists so deterministic tests can force the
    /// <c>write_coordination_timeout</c>/<c>wiki.write_lock.timeout</c> path without a
    /// multi-second wait; production callers leave this <c>null</c>.
    /// </param>
    /// <param name="logPath">
    /// ADR-017 (014-wiki-storage-restructure): the run's <c>log.md</c> path (any rooted
    /// form — canonicalized the same way every guarded-write target is). When supplied
    /// together with <paramref name="writeLocksDir"/>, every guarded write to this exact
    /// target is additionally evaluated against the log-entry format check
    /// (append-only, heading shape, following paragraph — <see cref="SharedFileWriteGuard"/>).
    /// <c>null</c> (the default) disables the check, matching every pre-ADR-017 caller.
    /// </param>
    /// <param name="indexPath">
    /// ADR-017 (014-wiki-storage-restructure, US4): the run's <c>index.md</c> path (any
    /// rooted form — canonicalized the same way every guarded-write target is). When
    /// supplied together with <paramref name="writeLocksDir"/>, every guarded write to
    /// this exact target is additionally evaluated against the catalog-entry format check
    /// (every newly added <c>- [</c>-led line must match the link-description-status
    /// shape — <see cref="SharedFileWriteGuard"/>). <c>null</c> (the default) disables
    /// the check, matching every pre-US4 caller.
    /// </param>
    /// <param name="activitySource">
    /// The calling agent process's own frozen <see cref="ActivitySource"/> (ADR-005/
    /// ADR-013), used only to emit the <c>guardrails.format_validate</c> span around the
    /// <paramref name="logPath"/>/<paramref name="indexPath"/> format checks. <c>null</c>
    /// (the default) emits no span.
    /// </param>
    public GuardedToolExecutor(
        SafetyPolicy policy,
        WriteJournal journal,
        string repositoryRoot,
        string? taskId = null,
        ToolRegistry? registry = null,
        IToolCallInstrumentation? instrumentation = null,
        string? writeLocksDir = null,
        TimeSpan? writeLockBackoffCap = null,
        string? logPath = null,
        string? indexPath = null,
        ActivitySource? activitySource = null)
    {
        _policy = policy;
        _journal = journal;
        _repositoryRoot = repositoryRoot;
        _taskId = taskId ?? string.Empty;
        _registry = registry ?? ToolRegistry.Default;
        _instrumentation = instrumentation ?? NullToolCallInstrumentation.Instance;
        _canonicalLogPath = logPath is not null ? Canonicalize(logPath) : null;
        var canonicalIndexPath = indexPath is not null ? Canonicalize(indexPath) : null;
        _writeGuard = writeLocksDir is not null
            ? new SharedFileWriteGuard(writeLocksDir, writeLockBackoffCap, _canonicalLogPath, canonicalIndexPath, activitySource)
            : null;
    }

    /// <summary>All policy denials that occurred during the run so far.</summary>
    public IReadOnlyList<DeniedActionRecord> Denials => _denials;

    /// <summary>All file paths successfully written during the run so far.</summary>
    public IReadOnlyList<string> TouchedPaths => _touchedPaths;

    /// <summary>
    /// ADR-015 (012-query-synthesis-writes): the subset of <see cref="TouchedPaths"/>
    /// whose matched write rule was <c>create-only</c> — i.e. pages newly created this
    /// run (as opposed to `index.md`/`log.md` appends, which are plain <c>read-write</c>
    /// targets). This is the harness-reported source for
    /// <see cref="Grimoire.AgentRuntime.RunEvents.RunCompletionMetadata.CreatedArtifacts"/>: mechanical, from
    /// the run's own journal — no judgment about page content (Constitution Principle V).
    /// </summary>
    public IReadOnlyList<string> CreatedPaths => _createdPaths;

    /// <summary>
    /// 025-agent-owned-log (ADR-028, FR-012a): the run's allowed <em>wiki-content</em>
    /// writes — <see cref="TouchedPaths"/> minus the canonical activity-log path. Pure set
    /// arithmetic over the harness's own record of writes it allowed: no file is read, no
    /// content is inspected, and no judgment is made about what any write meant
    /// (Constitution Principle V).
    ///
    /// Deliberately derived from <see cref="TouchedPaths"/> and not
    /// <see cref="CreatedPaths"/>: the latter is create-only writes, which would miss an
    /// index-only or page-update run — both of which the spec counts as wiki changes.
    /// </summary>
    public IReadOnlyList<string> WikiContentWrites =>
        _canonicalLogPath is null
            ? _touchedPaths
            : [.. _touchedPaths.Where(p => !string.Equals(p, _canonicalLogPath, StringComparison.Ordinal))];

    /// <summary>
    /// 025-agent-owned-log (ADR-028, FR-012a): whether the canonical activity-log path is
    /// among this run's successfully written paths. <c>false</c> when no log path was
    /// configured for the run.
    /// </summary>
    public bool ActivityLogWritten =>
        _canonicalLogPath is not null
        && _touchedPaths.Contains(_canonicalLogPath, StringComparer.Ordinal);

    /// <summary>
    /// Executes one tool call, applying policy, journaling, and telemetry.
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string inputJson,
        int turn,
        CancellationToken cancellationToken)
    {
        // A tool name this run's registry does not offer is always rejected as unknown —
        // even if a dispatch case for it exists below — so a read-only-configured
        // executor (Grimoire.QueryAgent) can never reach the write branch regardless of
        // what the model requests (ADR-011 R3, FR-011).
        if (!_registry.Supports(toolName))
        {
            return new ToolExecutionResult(true, $"Unknown tool: {toolName}");
        }

        switch (toolName)
        {
            case ToolRegistry.ListFiles:
                return await ExecuteListFilesAsync(inputJson, turn, cancellationToken);
            case ToolRegistry.ReadFile:
                return await ExecuteReadFileAsync(inputJson, turn, cancellationToken);
            case ToolRegistry.WriteFile:
                return await ExecuteWriteFileAsync(inputJson, turn, cancellationToken);
            default:
                return new ToolExecutionResult(true, $"Unknown tool: {toolName}");
        }
    }

    // ── list_files ───────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteListFilesAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetStringProperty(inputJson, "path", out var relativePath) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return new ToolExecutionResult(true, "Missing required property: path");
        }

        var canonical = Canonicalize(relativePath);
        var policyResult = _policy.Evaluate(canonical, isWrite: false);

        if (!policyResult.IsAllowed)
        {
            return RecordDenial(ToolRegistry.ListFiles, relativePath, canonical, policyResult.DenialReason!, turn);
        }

        _instrumentation.RecordAllowed(_taskId, ToolRegistry.ListFiles, canonical, turn);

        if (!Directory.Exists(canonical))
        {
            return new ToolExecutionResult(true, $"Directory not found: {relativePath}");
        }

        var entries = new StringBuilder();
        foreach (var dir in Directory.GetDirectories(canonical).OrderBy(d => d, StringComparer.Ordinal))
        {
            entries.AppendLine(Path.GetRelativePath(_repositoryRoot, dir).Replace('\\', '/') + "/");
        }
        foreach (var file in Directory.GetFiles(canonical).OrderBy(f => f, StringComparer.Ordinal))
        {
            entries.AppendLine(Path.GetRelativePath(_repositoryRoot, file).Replace('\\', '/'));
        }

        return new ToolExecutionResult(false, entries.ToString().TrimEnd());
    }

    // ── read_file ────────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteReadFileAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetStringProperty(inputJson, "path", out var relativePath) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return new ToolExecutionResult(true, "Missing required property: path");
        }

        var canonical = Canonicalize(relativePath);
        var policyResult = _policy.Evaluate(canonical, isWrite: false);

        if (!policyResult.IsAllowed)
        {
            return RecordDenial(ToolRegistry.ReadFile, relativePath, canonical, policyResult.DenialReason!, turn);
        }

        _instrumentation.RecordAllowed(_taskId, ToolRegistry.ReadFile, canonical, turn);

        if (!File.Exists(canonical))
        {
            return new ToolExecutionResult(true, $"File not found: {relativePath}");
        }

        var content = await File.ReadAllTextAsync(canonical, Encoding.UTF8, cancellationToken);
        _writeGuard?.OnReadFile(canonical, content);
        return new ToolExecutionResult(false, content);
    }

    // ── write_file ───────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteWriteFileAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetStringProperty(inputJson, "path", out var relativePath) ||
            string.IsNullOrWhiteSpace(relativePath))
        {
            return new ToolExecutionResult(true, "Missing required property: path");
        }

        if (!TryGetStringProperty(inputJson, "content", out var content))
        {
            return new ToolExecutionResult(true, "Missing required property: content");
        }

        var canonical = Canonicalize(relativePath);
        var policyResult = _policy.Evaluate(canonical, isWrite: true);

        if (!policyResult.IsAllowed)
        {
            return RecordDenial(ToolRegistry.WriteFile, relativePath, canonical, policyResult.DenialReason!, turn);
        }

        // ADR-015: coordinate with other writers (Ingest/Query/future Lint) sharing the
        // same guarded tool boundary. Absent (no writeLocksDir supplied), this is a no-op
        // and behavior is unchanged from before this feature.
        IDisposable? lockHandle = null;
        if (_writeGuard is not null)
        {
            // T042 (012-query-synthesis-writes, US3): plan.md's `guardrails.acquire_write_lock`
            // span covers exactly this acquisition attempt (lock acquire + create-only/CAS
            // decision, taken together since the lock is held for both — contract §3). The
            // corresponding `*_agent.tool_call` span for this same write is only created
            // afterward (RecordAllowed/RecordDenied), once the final decision is known, so it
            // is not yet Activity.Current here; see IToolCallInstrumentation's doc comment.
            using var lockActivity = _instrumentation.StartAcquireWriteLockActivity(_taskId, canonical, turn);
            var stopwatch = Stopwatch.StartNew();
            var guardDecision = await _writeGuard.EvaluateWriteAsync(canonical, policyResult.Mode, content, cancellationToken);
            stopwatch.Stop();

            // "timeout" only for write_coordination_timeout — every other denial reason
            // (create_only_target_exists, write_conflict_stale_read) and the allow path all
            // required the lock to actually be acquired first (contract §3).
            var lockOutcome = guardDecision.DenialReason == "write_coordination_timeout" ? "timeout" : "acquired";
            var waitSeconds = stopwatch.Elapsed.TotalSeconds;

            lockActivity?.SetTag("path", canonical);
            lockActivity?.SetTag("outcome", lockOutcome);
            lockActivity?.SetTag("wait_ms", stopwatch.Elapsed.TotalMilliseconds);

            _instrumentation.RecordWriteLockAcquisition(_taskId, canonical, lockOutcome, waitSeconds, turn);

            if (!guardDecision.IsAllowed)
            {
                var reason = guardDecision.DenialReason!;

                // ADR-015 (012-query-synthesis-writes): create_only_target_exists and
                // write_conflict_stale_read are write-coordination rejections, distinct from
                // write_coordination_timeout (its own wiki.write_lock.timeout signal) and
                // from the pre-existing out_of_scope/no_rule/traversal policy-scope denials
                // (their own established RecordDenied-only signals) — plan.md's
                // wiki.write_conflict.rejected/wiki.write_conflict.rejections_total rows.
                // ADR-017 (014-wiki-storage-restructure) extends this same signal to all
                // four of its new denial reasons (plan.md ## Observability: "reused
                // unchanged for ADR-017's four new denial reasons") — three from US3's
                // log.md format check and one, catalog_entry_malformed, from US4's
                // index.md check (found missing here by /speckit-analyze's T060
                // remediation, which needed all four wired to write a passing test).
                if (reason is "create_only_target_exists" or "write_conflict_stale_read"
                    or "log_entry_not_prepended" or "log_entry_malformed_heading" or "log_entry_missing_paragraph"
                    or "catalog_entry_malformed")
                {
                    _instrumentation.RecordWriteConflictRejected(_taskId, canonical, reason, turn);
                }

                return RecordDenial(ToolRegistry.WriteFile, relativePath, canonical, reason, turn);
            }

            lockHandle = guardDecision.LockHandle;
        }

        try
        {
            // Executor obligations (contract order):
            // 1. Journal prior state.
            await _journal.RecordAsync(canonical, cancellationToken);

            // 2. Create parent dirs inside the write scope.
            var parentDir = Path.GetDirectoryName(canonical);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // 3. Atomic write via temp + rename within the same directory.
            var tempPath = canonical + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, cancellationToken);
                File.Move(tempPath, canonical, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
                }
                throw;
            }

            // 4. Update the write guard's read-hash baseline for this path (so this run's
            // own next write/read of the same path is never mistaken for a stale read).
            _writeGuard?.OnWriteCommitted(canonical, content);
        }
        finally
        {
            // Release the coordination lock unconditionally — including on failure or
            // cancellation — so an interrupted run can never wedge a target (ADR-015,
            // FR-011).
            lockHandle?.Dispose();
        }

        // 5. Record touched path, emit telemetry.
        _touchedPaths.Add(canonical);
        _instrumentation.RecordAllowed(_taskId, ToolRegistry.WriteFile, canonical, turn);

        // ADR-015: a create-only write that reaches here has succeeded — this run created
        // a brand-new page (the existence check in step "guardDecision" above already
        // ruled out an overwrite). Mechanical bookkeeping only; no content judgment.
        if (policyResult.IsCreateOnly)
        {
            _createdPaths.Add(canonical);
            _instrumentation.RecordCreateOnlyWriteSucceeded(_taskId, canonical, turn);
        }

        return new ToolExecutionResult(false, $"Written: {relativePath}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private ToolExecutionResult RecordDenial(string action, string requestedTarget, string canonicalTarget, string reason, int turn)
    {
        var record = new DeniedActionRecord(action, requestedTarget, canonicalTarget, reason, turn);
        _denials.Add(record);

        _instrumentation.RecordDenied(_taskId, action, requestedTarget, canonicalTarget, reason, turn);

        return new ToolExecutionResult(
            true,
            $"denied: {reason}. This action is outside the safety policy; continue with your remaining allowed work.");
    }

    /// <summary>
    /// Resolves a repo-root-relative (or absolute) path to a canonical absolute path.
    /// Applies lexical normalization and resolves symbolic links for existing
    /// path segments so policy evaluation is performed on the physical target.
    /// </summary>
    private string Canonicalize(string path)
    {
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_repositoryRoot, path));

        return ResolvePhysicalPathInRepository(fullPath);
    }

    private string ResolvePhysicalPathInRepository(string fullPath)
    {
        var canonical = Path.GetFullPath(fullPath);

        if (!IsWithinRepositoryRoot(canonical))
        {
            return canonical;
        }

        var relative = Path.GetRelativePath(_repositoryRoot, canonical);
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = _repositoryRoot;
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);

            if (!TryResolveLinkTarget(current, out var targetPath))
            {
                continue;
            }

            current = targetPath;
            for (var j = i + 1; j < parts.Length; j++)
            {
                current = Path.Combine(current, parts[j]);
            }

            break;
        }

        return Path.GetFullPath(current);
    }

    private bool IsWithinRepositoryRoot(string canonicalTarget)
    {
        var relative = Path.GetRelativePath(_repositoryRoot, canonicalTarget);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool TryResolveLinkTarget(string path, out string resolvedTarget)
    {
        resolvedTarget = string.Empty;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            FileSystemInfo info = Directory.Exists(path)
                ? new DirectoryInfo(path)
                : new FileInfo(path);

            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return false;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                return false;
            }

            resolvedTarget = Path.GetFullPath(target.FullName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetStringProperty(string json, string propertyName, out string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (JsonException) { }

        value = string.Empty;
        return false;
    }
}
