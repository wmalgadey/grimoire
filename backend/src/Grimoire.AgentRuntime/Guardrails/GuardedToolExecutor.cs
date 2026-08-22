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

    // ── search_files bounds (026-guarded-tool-surface, ADR-030 R2/R5) ──────────────────
    // Single source of truth for the four documented defaults (research.md D2-D5). Kept as
    // compile-time constants deliberately: SearchRegexBoundaryRuleTests decodes the options
    // argument straight off the IL, which only works when it is a literal, not a value
    // computed at runtime.
    private const RegexOptions SearchRegexOptions = RegexOptions.NonBacktracking;
    private const int SearchDefaultResultCap = 200;
    private const int SearchHardResultCeiling = 1000;
    private const int SearchMaxPatternLength = 1000;

    // research.md D3: one documented "search time budget" bound, reused for both purposes
    // it serves — the Regex constructor's own per-match timeout (never a hung run on one
    // pathological line) and the wall-clock budget for the whole multi-file scan. An
    // instance field (not another const) so tests can override it via the constructor —
    // mirroring writeLockBackoffCap's rationale below — while staying a single-instruction
    // load (ldfld) at the Regex construction call site, which is all
    // SearchRegexBoundaryRuleTests' IL scan requires of it.
    private static readonly TimeSpan DefaultSearchTimeBudget = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _searchTimeBudget;

    private readonly SafetyPolicy _policy;
    private readonly WriteJournal _journal;
    private readonly string _repositoryRoot;
    private readonly string _taskId;
    private readonly ToolRegistry _registry;
    private readonly IToolCallInstrumentation _instrumentation;
    private readonly SharedFileWriteGuard? _writeGuard;
    private readonly string? _canonicalLogPath;
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
    /// <param name="searchTimeBudget">
    /// 026-guarded-tool-surface (ADR-030 R2/R5): overrides <c>search_files</c>' default
    /// 2-second time budget (both the Regex match timeout and the overall multi-file scan
    /// budget). Exists so deterministic tests can force the <c>wiki.search.timed_out</c>
    /// path without a multi-second wait, mirroring <paramref name="writeLockBackoffCap"/>;
    /// production callers leave this <c>null</c>.
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
        ActivitySource? activitySource = null,
        TimeSpan? searchTimeBudget = null)
    {
        _policy = policy;
        _journal = journal;
        _repositoryRoot = repositoryRoot;
        _taskId = taskId ?? string.Empty;
        _registry = registry ?? ToolRegistry.Default;
        _instrumentation = instrumentation ?? NullToolCallInstrumentation.Instance;
        _searchTimeBudget = searchTimeBudget ?? DefaultSearchTimeBudget;
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
            case ToolRegistry.SearchFiles:
                return await ExecuteSearchFilesAsync(inputJson, turn, cancellationToken);
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

    // ── search_files ─────────────────────────────────────────────────────────────
    // ADR-030 R1/R2 (026-guarded-tool-surface): mimics `grep -rn`. Every candidate file is
    // evaluated against the read policy before it is opened (T016) — a denied match is
    // omitted silently, never reported, because reporting it would itself disclose that the
    // path exists. A denial is recorded only for the search's own `path` root, exactly like
    // list_files/read_file (T017).

    private async Task<ToolExecutionResult> ExecuteSearchFilesAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        // Every candidate file is read synchronously (File.ReadLines, for lazy line-by-line
        // scanning under the time budget below) — no genuine async I/O in this dispatch, but
        // the signature stays Task<ToolExecutionResult> to match every sibling dispatch
        // method's shape.
        await Task.CompletedTask;

        if (!TryGetStringProperty(inputJson, "pattern", out var pattern) || pattern.Length == 0)
        {
            return new ToolExecutionResult(true, "Missing required property: pattern");
        }

        if (pattern.Length > SearchMaxPatternLength)
        {
            return RecordSearchPatternRejected(pattern, "pattern_too_long", turn);
        }

        if (!TryBuildSearchRegex(inputJson, pattern, out var regex, out var rejectionReason))
        {
            return RecordSearchPatternRejected(pattern, rejectionReason!, turn);
        }

        var hasPathPrefix = TryGetStringProperty(inputJson, "path", out var relativePathPrefix) &&
            !string.IsNullOrWhiteSpace(relativePathPrefix);
        var searchRoot = hasPathPrefix ? Canonicalize(relativePathPrefix) : _repositoryRoot;

        // ADR-030 R1: "A denial is recorded only for the search's own path argument when
        // that root is out of scope." A default (whole-repository) search has no such
        // argument to be out of scope — every candidate file is filtered silently below
        // instead (ScanCandidateFiles), never as a top-level denial. Gating the default
        // search on a root-level policy check would wrongly deny any search whose read
        // scope is narrower than the repository root, even though per-file filtering
        // already makes that search incapable of widening the read scope.
        if (hasPathPrefix)
        {
            var rootPolicyResult = _policy.Evaluate(searchRoot, isWrite: false);
            if (!rootPolicyResult.IsAllowed)
            {
                var denial = RecordDenial(ToolRegistry.SearchFiles, relativePathPrefix, searchRoot, rootPolicyResult.DenialReason!, turn);
                _instrumentation.RecordSearchInvocation(_taskId, "denied", matchesReturned: 0, filesScanned: 0, turn);
                return denial;
            }
        }

        _instrumentation.RecordAllowed(_taskId, ToolRegistry.SearchFiles, searchRoot, turn);

        var requestedMaxResults = TryGetIntProperty(inputJson, "max_results", out var maxResultsValue)
            ? maxResultsValue
            : SearchDefaultResultCap;
        var cap = Math.Clamp(requestedMaxResults, 1, SearchHardResultCeiling);

        using var scanActivity = _instrumentation.StartSearchScanActivity(_taskId, turn);
        var scan = ScanCandidateFiles(searchRoot, regex!, cap, cancellationToken);

        var outcome = scan.Truncated ? "truncated" : scan.TimedOut ? "timed_out" : "completed";
        _instrumentation.RecordSearchInvocation(_taskId, outcome, scan.Matches.Count, scan.FilesScanned, turn);
        EmitSearchScanTags(scanActivity, pattern.Length, hasPathPrefix ? relativePathPrefix : ".", scan, outcome);

        return BuildSearchResult(scan, pattern.Length, cap, turn);
    }

    private void EmitSearchScanTags(Activity? scanActivity, int patternLength, string pathPrefix, SearchScanResult scan, string outcome)
    {
        scanActivity?.SetTag("task_id", _taskId);
        scanActivity?.SetTag("pattern_length", patternLength);
        scanActivity?.SetTag("path_prefix", pathPrefix);
        scanActivity?.SetTag("files_scanned", scan.FilesScanned);
        scanActivity?.SetTag("matches", scan.Matches.Count);
        scanActivity?.SetTag("truncated", scan.Truncated);
        scanActivity?.SetTag("outcome", outcome);
    }

    /// <summary>
    /// ignore_case is folded into the pattern itself (an inline <c>(?i)</c> modifier) rather
    /// than into the RegexOptions argument, so that argument stays the literal constant
    /// <see cref="SearchRegexOptions"/> at every call site (T020) —
    /// SearchRegexBoundaryRuleTests verifies the NonBacktracking bit by decoding a
    /// compile-time constant off the IL, which a runtime-computed
    /// <c>SearchRegexOptions | (ignoreCase ? ... : ...)</c> would defeat.
    /// </summary>
    private bool TryBuildSearchRegex(string inputJson, string pattern, out Regex? regex, out string? rejectionReason)
    {
        var ignoreCase = TryGetBoolProperty(inputJson, "ignore_case", out var ignoreCaseValue) && ignoreCaseValue;
        var effectivePattern = ignoreCase ? "(?i)" + pattern : pattern;

        try
        {
            regex = new Regex(effectivePattern, SearchRegexOptions, _searchTimeBudget);
            rejectionReason = null;
            return true;
        }
        catch (NotSupportedException)
        {
            // The NonBacktracking engine rejects constructs it cannot run without
            // backtracking (lookaround, backreferences) at construction time (ADR-030 R2).
            regex = null;
            rejectionReason = "unsupported_syntax";
            return false;
        }
        catch (ArgumentException)
        {
            regex = null;
            rejectionReason = "invalid_pattern";
            return false;
        }
    }

    private sealed record SearchScanResult(List<string> Matches, int FilesScanned, bool Truncated, bool TimedOut);

    private SearchScanResult ScanCandidateFiles(string searchRoot, Regex regex, int cap, CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        var filesScanned = 0;
        var truncated = false;
        var timedOut = false;
        var stopwatch = Stopwatch.StartNew();

        var candidateFiles = EnumerateSearchCandidates(searchRoot).OrderBy(f => f, StringComparer.Ordinal);

        foreach (var candidateFile in candidateFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopwatch.Elapsed >= _searchTimeBudget)
            {
                timedOut = true;
                break;
            }

            // T016 (ADR-030 R1): evaluated per candidate path against the read policy — a
            // denied file is skipped, never reported as a denial.
            if (!_policy.Evaluate(candidateFile, isWrite: false).IsAllowed)
            {
                continue;
            }

            filesScanned++;

            var (fileTruncated, fileTimedOut) = ScanOneFile(candidateFile, regex, cap, stopwatch, matches);
            truncated |= fileTruncated;
            timedOut |= fileTimedOut;

            if (truncated || timedOut)
            {
                break;
            }
        }

        stopwatch.Stop();
        return new SearchScanResult(matches, filesScanned, truncated, timedOut);
    }

    /// <summary>
    /// `path` may name a single file or a directory to recurse into (contracts/
    /// guarded-tool-surface.md: "directory or file prefix").
    /// </summary>
    private IEnumerable<string> EnumerateSearchCandidates(string searchRoot)
    {
        if (File.Exists(searchRoot))
        {
            return [ResolvePhysicalPathInRepository(searchRoot)];
        }

        if (Directory.Exists(searchRoot))
        {
            return Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories).Select(ResolvePhysicalPathInRepository);
        }

        return [];
    }

    private (bool Truncated, bool TimedOut) ScanOneFile(string candidateFile, Regex regex, int cap, Stopwatch stopwatch, List<string> matches)
    {
        var lineNumber = 0;
        try
        {
            foreach (var line in File.ReadLines(candidateFile))
            {
                lineNumber++;

                if (stopwatch.Elapsed >= _searchTimeBudget)
                {
                    return (Truncated: false, TimedOut: true);
                }

                bool isMatch;
                try
                {
                    isMatch = regex.IsMatch(line);
                }
                catch (RegexMatchTimeoutException)
                {
                    return (Truncated: false, TimedOut: true);
                }

                if (!isMatch)
                {
                    continue;
                }

                var relativeMatchPath = Path.GetRelativePath(_repositoryRoot, candidateFile).Replace('\\', '/');
                matches.Add($"{relativeMatchPath}:{lineNumber}:{line}");

                if (matches.Count >= cap)
                {
                    return (Truncated: true, TimedOut: false);
                }
            }
        }
        catch (IOException)
        {
            // Unreadable (binary, locked, deleted mid-scan) — skip this file rather than
            // fail the whole search.
        }

        return (Truncated: false, TimedOut: false);
    }

    private ToolExecutionResult BuildSearchResult(SearchScanResult scan, int patternLength, int cap, int turn)
    {
        var result = new StringBuilder();
        foreach (var match in scan.Matches)
        {
            result.AppendLine(match);
        }

        if (scan.Truncated)
        {
            _instrumentation.LogSearchTruncated(_taskId, patternLength, cap, turn);
            result.AppendLine($"[truncated: showing the first {cap} matches]");
        }
        else if (scan.TimedOut)
        {
            _instrumentation.LogSearchTimedOut(_taskId, _searchTimeBudget.TotalMilliseconds, scan.FilesScanned, turn);
            result.AppendLine($"[incomplete: search time budget exceeded after scanning {scan.FilesScanned} file(s)]");
        }

        if (scan.Matches.Count == 0 && !scan.TimedOut)
        {
            return new ToolExecutionResult(false, "No matches found.");
        }

        return new ToolExecutionResult(false, result.ToString().TrimEnd());
    }

    private ToolExecutionResult RecordSearchPatternRejected(string pattern, string reason, int turn)
    {
        var record = new DeniedActionRecord(ToolRegistry.SearchFiles, pattern, pattern, reason, turn);
        _denials.Add(record);

        _instrumentation.RecordSearchInvocation(_taskId, "pattern_rejected", matchesReturned: 0, filesScanned: 0, turn);
        _instrumentation.LogSearchPatternRejected(_taskId, reason, pattern.Length, turn);

        return new ToolExecutionResult(
            true,
            $"denied: {reason}. This action is outside the safety policy; continue with your remaining allowed work.");
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

    private static bool TryGetIntProperty(string json, string propertyName, out int value)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetInt32(out value))
            {
                return true;
            }
        }
        catch (JsonException) { }

        value = 0;
        return false;
    }

    private static bool TryGetBoolProperty(string json, string propertyName, out bool value)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                value = prop.GetBoolean();
                return true;
            }
        }
        catch (JsonException) { }

        value = false;
        return false;
    }
}
