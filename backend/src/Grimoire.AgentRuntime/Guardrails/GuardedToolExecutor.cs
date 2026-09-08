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

    // ── batch bounds (026-guarded-tool-surface, ADR-030 R4/R5) ──────────────────────────
    private const int BatchMaxCalls = 20;
    private static readonly HashSet<string> BatchAllowedTools = new(StringComparer.Ordinal)
    {
        ToolRegistry.ListFiles, ToolRegistry.ReadFile, ToolRegistry.SearchFiles,
    };

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
    private readonly List<(string Path, int Turn)> _deletedPaths = [];
    // 028-lint-at-scale (US2, FR-004): canonical paths named in a successful read_file
    // result (any mode), a batch member's read result, or a search_files match — never a
    // denied path, never a bare list_files result (data-model.md ConsideredPaths). Feeds
    // WikiCoverage, computed once at run completion.
    private readonly HashSet<string> _consideredPaths = new(StringComparer.Ordinal);

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
    /// ADR-031 R4 (026-guarded-tool-surface): paths this run successfully deleted, paired
    /// with the turn each deletion happened on. The turn is carried because a later
    /// rollback (triggered by run failure, after the turn loop has already ended) needs it
    /// to correlate its <c>wiki.page.delete_rolled_back</c> log event back to the turn the
    /// original deletion occurred on.
    /// </summary>
    public IReadOnlyList<(string Path, int Turn)> DeletedPaths => _deletedPaths;

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
    /// 028-lint-at-scale (US2, FR-004): every distinct canonical page path this run
    /// actually opened — via <c>read_file</c> (any mode), a <c>batch</c> member's read, or
    /// a <c>search_files</c> match. A path named only in a <c>list_files</c> result is
    /// never added (data-model.md ConsideredPaths). Feeds <c>WikiCoverage</c>, computed
    /// once at run completion — never self-reported by the agent.
    /// </summary>
    public IReadOnlySet<string> ConsideredPaths => _consideredPaths;

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
            case ToolRegistry.DeleteFile:
                return await ExecuteDeleteFileAsync(inputJson, turn, cancellationToken);
            case ToolRegistry.Batch:
                return await ExecuteBatchAsync(inputJson, turn, cancellationToken);
            default:
                return new ToolExecutionResult(true, $"Unknown tool: {toolName}");
        }
    }

    /// <summary>
    /// The canonicalize-or-deny → policy-evaluate-or-deny sequence shared by every
    /// dispatch method that authorizes a single path (list_files/read_file/write_file/
    /// delete_file). Does not record the allowed telemetry itself: write_file defers
    /// that until after the write actually commits, so callers emit
    /// <see cref="IToolCallInstrumentation.RecordAllowed"/> themselves once authorized.
    /// </summary>
    private bool TryAuthorize(
        string toolName,
        string relativePath,
        Func<string, PolicyDecision> evaluate,
        int turn,
        out string canonical,
        out PolicyDecision policyResult,
        out ToolExecutionResult? denial)
    {
        if (!TryCanonicalize(relativePath, out canonical, out var canonicalizationDenialReason))
        {
            policyResult = PolicyDecision.Deny(canonicalizationDenialReason!);
            denial = RecordDenial(toolName, relativePath, relativePath, canonicalizationDenialReason!, turn);
            return false;
        }

        policyResult = evaluate(canonical);
        if (!policyResult.IsAllowed)
        {
            denial = RecordDenial(toolName, relativePath, canonical, policyResult.DenialReason!, turn);
            return false;
        }

        denial = null;
        return true;
    }

    /// <summary>Shared "path" property extraction for every single-path dispatch method.</summary>
    private static bool TryGetRequiredPath(string inputJson, out string relativePath, out ToolExecutionResult? missingError)
    {
        if (!TryGetStringProperty(inputJson, "path", out relativePath) || string.IsNullOrWhiteSpace(relativePath))
        {
            missingError = new ToolExecutionResult(true, "Missing required property: path");
            return false;
        }

        missingError = null;
        return true;
    }

    // ── list_files ───────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteListFilesAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredPath(inputJson, out var relativePath, out var missingError))
        {
            return missingError!;
        }

        if (!TryAuthorize(ToolRegistry.ListFiles, relativePath, p => _policy.Evaluate(p, isWrite: false), turn,
            out var canonical, out _, out var denial))
        {
            return denial!;
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
    // ADR-030 R3 (026-guarded-tool-surface, Feature-Scoped Invariant): optional
    // offset/limit/frontmatter_only make a read partial. A partial read MUST NOT call
    // _writeGuard.OnReadFile (T049) — feeding it a fragment would make a later stale-read
    // conflict undetectable, silently licensing a whole-file overwrite off a slice the
    // agent never actually saw in full. Omitting all three stays byte-for-byte today's
    // whole-file read (FR-009), baseline included.

    private async Task<ToolExecutionResult> ExecuteReadFileAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredPath(inputJson, out var relativePath, out var missingError))
        {
            return missingError!;
        }

        if (!TryParseReadRangeRequest(inputJson, out var range, out var rangeError))
        {
            return new ToolExecutionResult(true, rangeError!);
        }

        if (!TryAuthorize(ToolRegistry.ReadFile, relativePath, p => _policy.Evaluate(p, isWrite: false), turn,
            out var canonical, out _, out var denial))
        {
            return denial!;
        }

        _instrumentation.RecordAllowed(_taskId, ToolRegistry.ReadFile, canonical, turn);

        if (!File.Exists(canonical))
        {
            return new ToolExecutionResult(true, $"File not found: {relativePath}");
        }

        var content = await File.ReadAllTextAsync(canonical, Encoding.UTF8, cancellationToken);

        // Copilot review (PR #207): recorded only once the read has actually succeeded — a
        // path is not "considered" (ConsideredPaths' contract, above) if ReadAllTextAsync
        // throws (IO/permissions) before this line is reached.
        _consideredPaths.Add(canonical);

        if (!range.HasOffset && !range.HasLimit && !range.FrontmatterOnly)
        {
            _writeGuard?.OnReadFile(canonical, content);
            _instrumentation.RecordReadInvocation(_taskId, "full", turn);
            return new ToolExecutionResult(false, content);
        }

        if (range.FrontmatterOnly)
        {
            _instrumentation.RecordReadInvocation(_taskId, "frontmatter", turn);
            return new ToolExecutionResult(false, ExtractFrontmatter(content));
        }

        _instrumentation.RecordReadInvocation(_taskId, "range", turn);
        return new ToolExecutionResult(false, ExtractLineRange(content, range.HasOffset ? range.Offset : 1, range.HasLimit ? range.Limit : null));
    }

    /// <summary>
    /// Copilot review (PR #177): offset/limit are documented as 1-based/positive-count
    /// (data-model.md's ReadRequest, "1-based first line" / "maximum number of lines").
    /// A non-positive value is rejected outright rather than silently coerced (offset=0
    /// becoming line 1, limit=0 becoming an empty slice with no EOF marker) — the same
    /// "malformed input is a denial, not a reinterpretation" stance search_files already
    /// takes for an oversized pattern. Split out of <see cref="ExecuteReadFileAsync"/> to
    /// keep that method's own cyclomatic complexity under the CI gate.
    /// <paramref name="request"/> is a plain value tuple rather than a named record type
    /// deliberately: introducing a standalone type declaration here previously confused
    /// the CI complexity tool's lightweight C# parser into misattributing an unrelated,
    /// unchanged method (<see cref="ExecuteWriteFileAsync"/>) to a different pseudo-context
    /// name between the base branch and this one, which made the complexity gate's
    /// base/head function matching see it as a brand new function and fail spuriously.
    /// </summary>
    private static bool TryParseReadRangeRequest(
        string inputJson,
        out (bool HasOffset, int Offset, bool HasLimit, int Limit, bool FrontmatterOnly) request,
        out string? error)
    {
        request = default;

        var hasOffset = TryGetIntProperty(inputJson, "offset", out var offset);
        if (hasOffset && offset < 1)
        {
            error = "Invalid property: offset must be a 1-based line number (>= 1).";
            return false;
        }

        var hasLimit = TryGetIntProperty(inputJson, "limit", out var limit);
        if (hasLimit && limit < 1)
        {
            error = "Invalid property: limit must be >= 1.";
            return false;
        }

        var frontmatterOnly = TryGetBoolProperty(inputJson, "frontmatter_only", out var frontmatterOnlyValue) && frontmatterOnlyValue;

        error = null;
        request = (hasOffset, offset, hasLimit, limit, frontmatterOnly);
        return true;
    }

    /// <summary>
    /// "The frontmatter block" is the content between a leading document's first two
    /// <c>---</c> delimiters (the same convention <c>IngestTaskArtifactFrontmatter</c> parses).
    /// A document with no frontmatter (no leading <c>---</c>, or no closing delimiter)
    /// has none to return.
    /// </summary>
    private static string ExtractFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var sections = content.Split("---", 3, StringSplitOptions.None);
        return sections.Length < 3 ? string.Empty : sections[1].Trim();
    }

    /// <summary>
    /// 1-based, inclusive-of-<paramref name="offset"/> line range, like <c>sed -n 'X,Yp'</c>.
    /// An offset at or beyond the last line returns only the end-of-file marker (spec.md
    /// Edge Cases: "an empty or partial result with an explicit end-of-file signal, not an
    /// error that ends the run") — never <see cref="ToolExecutionResult.IsError"/>. A
    /// <paramref name="limit"/> that would run past the last line is honored up to the last
    /// line and the same marker is appended, since the request could not be satisfied in
    /// full. A <paramref name="limit"/> that lands exactly on the last line is fully
    /// satisfiable and gets no marker — that request was never truncated (Copilot review,
    /// PR #177: the prior <c>&gt;=</c> comparison marked this case as end-of-file too).
    /// </summary>
    private static string ExtractLineRange(string content, int offset, int? limit)
    {
        var lines = SplitIntoLines(content);
        var totalLines = lines.Length;
        var startIndex = Math.Max(offset - 1, 0);

        if (startIndex >= totalLines)
        {
            return $"[end of file: {totalLines} line(s) total]";
        }

        var available = totalLines - startIndex;
        var truncatedByEndOfFile = limit.HasValue && limit.Value > available;
        var count = limit.HasValue ? Math.Min(limit.Value, available) : available;
        var slice = string.Join('\n', lines.Skip(startIndex).Take(count));

        return truncatedByEndOfFile
            ? $"{slice}\n[end of file: {totalLines} line(s) total]"
            : slice;
    }

    /// <summary>
    /// Splits on <c>\n</c> after normalizing <c>\r\n</c>, treating a single trailing
    /// newline as terminating the last line rather than introducing an extra empty one —
    /// matching how a line count is conventionally reported (an editor's line count, or
    /// <c>wc -l</c> on a file ending in a newline), not <see cref="string.Split(char[])"/>'s
    /// literal per-separator split.
    /// </summary>
    private static string[] SplitIntoLines(string content)
    {
        if (content.Length == 0)
        {
            return [];
        }

        var normalized = content.Replace("\r\n", "\n");
        var trimmed = normalized.EndsWith('\n') ? normalized[..^1] : normalized;
        return trimmed.Split('\n');
    }

    // ── write_file ───────────────────────────────────────────────────────────────

    private async Task<ToolExecutionResult> ExecuteWriteFileAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredPath(inputJson, out var relativePath, out var missingError))
        {
            return missingError!;
        }

        if (!TryGetStringProperty(inputJson, "content", out var content))
        {
            return new ToolExecutionResult(true, "Missing required property: content");
        }

        // 028-lint-at-scale (US3, FSI-1, FR-010): optional call-shape parameter, distinct
        // from the policy-level Grimoire.Domain.Guardrails.WriteMode (data-model.md "Two
        // distinct 'mode' concepts"). Anything other than exactly "prepend" (omitted,
        // "replace", or an unrecognized value) is treated as the default, unchanged
        // full-content behavior — fails open to the pre-existing shape rather than
        // erroring on a malformed value (Constitution V host stability guarantee).
        var isPrepend = TryGetStringProperty(inputJson, "mode", out var modeValue)
            && string.Equals(modeValue, "prepend", StringComparison.Ordinal);

        if (!TryAuthorize(ToolRegistry.WriteFile, relativePath, p => _policy.Evaluate(p, isWrite: true), turn,
            out var canonical, out var policyResult, out var denial))
        {
            return denial!;
        }

        // ADR-015: coordinate with other writers (Ingest/Query/future Lint) sharing the
        // same guarded tool boundary. Absent (no writeLocksDir supplied), this is a no-op
        // and behavior is unchanged from before this feature.
        var lockAcquisition = await AcquireWriteLockAsync(
            relativePath, canonical, policyResult.Mode, content, isPrepend, turn, cancellationToken);
        if (lockAcquisition.Denial is not null)
        {
            return lockAcquisition.Denial;
        }

        bool published;
        try
        {
            published = await WriteFileAtomicallyAsync(canonical, lockAcquisition.ResolvedContent, cancellationToken);
        }
        finally
        {
            // Release the coordination lock unconditionally — including on failure or
            // cancellation — so an interrupted run can never wedge a target (ADR-015,
            // FR-011).
            lockAcquisition.LockHandle?.Dispose();
        }

        // D3 (027-host-stability): the physical path was re-resolved immediately before
        // the publishing rename and no longer matched what policy validated above — a
        // path segment was swapped to a symlink after validation (TOCTOU). Deny rather
        // than have published through the now-different physical route.
        if (!published)
        {
            return RecordDenial(ToolRegistry.WriteFile, relativePath, canonical, "revalidation_failed", turn);
        }

        // Record touched path, emit telemetry.
        _touchedPaths.Add(canonical);
        _instrumentation.RecordAllowed(_taskId, ToolRegistry.WriteFile, canonical, turn);

        // ADR-015: a create-only write that reaches here has succeeded — this run created
        // a brand-new page (the existence check in AcquireWriteLockAsync above already
        // ruled out an overwrite). Mechanical bookkeeping only; no content judgment.
        if (policyResult.IsCreateOnly)
        {
            _createdPaths.Add(canonical);
            _instrumentation.RecordCreateOnlyWriteSucceeded(_taskId, canonical, turn);
        }

        return new ToolExecutionResult(false, $"Written: {relativePath}");
    }

    /// <summary>
    /// ADR-015 write-coordination step for <see cref="ExecuteWriteFileAsync"/>: when a
    /// <see cref="SharedFileWriteGuard"/> is configured, acquires its per-target lock and
    /// evaluates the create-only/CAS/format decision, emitting the
    /// <c>guardrails.acquire_write_lock</c> span and lock-acquisition telemetry either way.
    /// Absent a write guard, this is a no-op (<c>LockHandle: null, Denial: null</c>),
    /// matching pre-ADR-015 behavior exactly.
    /// <paramref name="mode"/> is a plain parameter (not a record type) deliberately — see
    /// <see cref="TryParseReadRangeRequest"/>'s doc comment on why a new type declaration
    /// here risks confusing the CI complexity tool's lightweight C# parser.
    /// </summary>
    private async Task<(IDisposable? LockHandle, ToolExecutionResult? Denial, string ResolvedContent)> AcquireWriteLockAsync(
        string relativePath, string canonical, WriteMode mode, string content, bool isPrepend, int turn, CancellationToken cancellationToken)
    {
        if (_writeGuard is null)
        {
            // 028-lint-at-scale (US3): prepend mode has no baseline/coordination
            // dependency of its own (research.md R8) — without a configured write guard
            // there is simply no lock to coordinate under, matching every other
            // guard-optional behavior in this method. No format-deviation signal fires on
            // this path either, mirroring the pre-existing "no guard configured ⇒ no
            // format check at all" behavior the logPath/indexPath parameters already
            // document.
            if (!isPrepend)
            {
                return (null, null, content);
            }

            var currentContentNoGuard = File.Exists(canonical)
                ? await File.ReadAllTextAsync(canonical, Utf8NoBom, cancellationToken)
                : string.Empty;
            return (null, null, content + currentContentNoGuard);
        }

        // T042 (012-query-synthesis-writes, US3): plan.md's `guardrails.acquire_write_lock`
        // span covers exactly this acquisition attempt (lock acquire + create-only/CAS
        // decision, taken together since the lock is held for both — contract §3). The
        // corresponding `*_agent.tool_call` span for this same write is only created
        // afterward (RecordAllowed/RecordDenied), once the final decision is known, so it
        // is not yet Activity.Current here; see IToolCallInstrumentation's doc comment.
        using var lockActivity = _instrumentation.StartAcquireWriteLockActivity(_taskId, canonical, turn);
        var stopwatch = Stopwatch.StartNew();
        var guardDecision = await _writeGuard.EvaluateWriteAsync(canonical, mode, content, cancellationToken, isPrepend);
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
            // ADR-017 (014-wiki-storage-restructure) extended this same signal to
            // index.md's catalog_entry_malformed denial (found missing here by
            // /speckit-analyze's T060 remediation, which needed it wired to write a
            // passing test). 028-lint-at-scale (US3, Clarifications 2026-08-27):
            // log.md's three format-check reasons (log_entry_not_prepended/
            // log_entry_malformed_heading/log_entry_missing_paragraph) no longer reach
            // this branch at all — they never deny — so they were removed from
            // IsWriteConflictReason below rather than left as dead cases.
            if (IsWriteConflictReason(reason))
            {
                _instrumentation.RecordWriteConflictRejected(_taskId, canonical, reason, turn);
            }

            return (null, RecordDenial(ToolRegistry.WriteFile, relativePath, canonical, reason, turn, guardDecision.Detail), content);
        }

        // 028-lint-at-scale (US3, FR-016, SC-009): a write that committed despite
        // deviating from the activity-log format contract's expected shape — recorded,
        // never denied. Mirrors RecordWriteConflictRejected's placement: the harness
        // owns turning a guard-reported fact into the agent-specific
        // metric/log-event/span signal (Constitution Principle V — no wiki-content
        // judgment here, just relaying what the guard found).
        if (guardDecision.FormatDeviationReasons is { Count: > 0 } deviations)
        {
            _instrumentation.RecordFormatDeviation(
                _taskId, canonical, isPrepend ? "prepend" : "replace", deviations, turn);
        }

        return (guardDecision.LockHandle, null, guardDecision.ResolvedContent ?? content);
    }

    private static bool IsWriteConflictReason(string reason) =>
        reason is "create_only_target_exists" or "write_conflict_stale_read" or "catalog_entry_malformed";

    /// <summary>
    /// The atomic-write half of <see cref="ExecuteWriteFileAsync"/>'s executor obligations
    /// (contract order): journal prior state, ensure the parent directory exists, write via
    /// temp file + rename, then update the write guard's read-hash baseline. Runs while the
    /// caller still holds any write-coordination lock. Returns <c>false</c> (D3,
    /// 027-host-stability) when the physical path re-resolved immediately before the
    /// publishing rename no longer matches <paramref name="canonical"/> — a path segment
    /// was swapped to a symlink after policy validation — without performing the rename;
    /// the caller records the <c>revalidation_failed</c> denial.
    /// </summary>
    private async Task<bool> WriteFileAtomicallyAsync(string canonical, string content, CancellationToken cancellationToken)
    {
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
        var published = false;
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, cancellationToken);

            // D3: re-resolve the physical path immediately before the rename that
            // publishes content at `canonical` — the smallest achievable window between
            // "path judged safe" and "path acted upon" (research.md D3). A mismatch means
            // a path segment became a symlink after validation; discard the pending temp
            // file instead of renaming through the now-different physical route.
            if (!RevalidatesToSamePhysicalPath(canonical))
            {
                return false;
            }

            File.Move(tempPath, canonical, overwrite: true);
            published = true;
        }
        finally
        {
            if (!published && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }

        // 4. Update the write guard's read-hash baseline for this path (so this run's
        // own next write/read of the same path is never mistaken for a stale read).
        _writeGuard?.OnWriteCommitted(canonical, content);
        return true;
    }

    // ── delete_file ──────────────────────────────────────────────────────────────
    // ADR-031 R3/R4 (026-guarded-tool-surface): mimics `rm`. Evaluated against the
    // **delete** scope (SafetyPolicy.EvaluateDelete), never the write scope — an agent
    // whose policy grants read-write on a prefix gains no deletion from it (R3). Journaled
    // before removal so a run that deletes and then fails restores it in the same
    // reverse-order rollback that restores an overwrite (R4, data-model.md's rollback
    // table: delete's prior state is the deleted content itself).

    private async Task<ToolExecutionResult> ExecuteDeleteFileAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredPath(inputJson, out var relativePath, out var missingError))
        {
            return missingError!;
        }

        if (!TryAuthorize(ToolRegistry.DeleteFile, relativePath, _policy.EvaluateDelete, turn,
            out var canonical, out _, out var denial))
        {
            return denial!;
        }

        // Recorded allowed, and the span opened, before the existence check — mirroring
        // read_file/list_files, so a policy-allowed attempt against an already-gone file
        // is still visible to the tool-call span/metric rather than silently invisible.
        _instrumentation.RecordAllowed(_taskId, ToolRegistry.DeleteFile, canonical, turn);
        using var deleteActivity = _instrumentation.StartDeleteFileActivity(_taskId, canonical, turn);

        if (!File.Exists(canonical))
        {
            deleteActivity?.SetTag("journaled", false);
            deleteActivity?.SetTag("outcome", "not_found");
            return new ToolExecutionResult(true, $"File not found: {relativePath}");
        }

        // D3 (027-host-stability): re-resolve the physical path immediately before the
        // mutating File.Delete call and compare it, ordinally, to the path already
        // validated against policy above. A mismatch means a path segment became a
        // symlink after validation (TOCTOU) — deny rather than delete through the now-
        // different physical route.
        if (!RevalidatesToSamePhysicalPath(canonical))
        {
            deleteActivity?.SetTag("journaled", false);
            deleteActivity?.SetTag("outcome", "revalidation_failed");
            return RecordDenial(ToolRegistry.DeleteFile, relativePath, canonical, "revalidation_failed", turn);
        }

        // Journal before removal: the recorded prior content is what a later rollback in
        // this run restores the file from.
        await _journal.RecordAsync(canonical, cancellationToken);
        File.Delete(canonical);

        _touchedPaths.Add(canonical);
        _deletedPaths.Add((canonical, turn));
        _instrumentation.RecordDeletion(_taskId, "applied", turn);
        _instrumentation.LogPageDeleted(_taskId, canonical, turn);

        deleteActivity?.SetTag("journaled", true);
        deleteActivity?.SetTag("outcome", "applied");

        return new ToolExecutionResult(false, $"Deleted: {relativePath}");
    }

    // ── batch ────────────────────────────────────────────────────────────────────
    // ADR-030 R4 (026-guarded-tool-surface): a batch admits only read-only tool names
    // (list_files, read_file, search_files) — no write, no delete, no nested batch — and
    // rejects wholesale, before any member executes, on any violation (T056). A batch that
    // passes validation carries no authority of its own: each member is dispatched through
    // the same ExecuteAsync entry point every top-level call goes through, so it is
    // evaluated against the policy and recorded exactly as if the model had called it
    // directly (T057) — RecordAllowed/RecordDenial fire per member, same as any other call.

    private async Task<ToolExecutionResult> ExecuteBatchAsync(
        string inputJson, int turn, CancellationToken cancellationToken)
    {
        if (!TryParseBatchCalls(inputJson, out var calls, out var parseError))
        {
            return new ToolExecutionResult(true, parseError!);
        }

        if (calls.Count > BatchMaxCalls)
        {
            return RecordBatchRejected("too_many_calls", calls.Count, turn);
        }

        foreach (var call in calls)
        {
            if (string.Equals(call.Tool, ToolRegistry.Batch, StringComparison.Ordinal))
            {
                return RecordBatchRejected("nested_batch", calls.Count, turn);
            }

            if (!BatchAllowedTools.Contains(call.Tool))
            {
                return RecordBatchRejected("tool_not_allowed_in_batch", calls.Count, turn);
            }
        }

        _instrumentation.RecordAllowed(_taskId, ToolRegistry.Batch, $"{calls.Count} call(s)", turn);
        using var batchActivity = _instrumentation.StartBatchActivity(_taskId, turn);

        var results = new StringBuilder();
        var deniedCount = 0;

        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];
            // Copilot review (PR #177): a member's IsError alone conflates a real policy
            // denial with a benign non-policy failure (file not found, malformed input) —
            // neither of which adds to _denials. denied_count means "policy denials", so
            // it is counted the same way SC-002/FR-013 define one: an entry actually
            // recorded in _denials during this member's dispatch.
            var denialsBeforeMember = _denials.Count;
            var memberResult = await ExecuteAsync(call.Tool, call.InputJson, turn, cancellationToken);
            var memberWasDenied = _denials.Count > denialsBeforeMember;
            if (memberWasDenied)
            {
                deniedCount++;
            }

            var memberStatus = memberWasDenied ? "denied" : memberResult.IsError ? "error" : "ok";
            results.AppendLine($"[{i}] {call.Tool}: {memberStatus}");
            results.AppendLine(memberResult.Content);
        }

        _instrumentation.RecordBatchInvocation(_taskId, "completed", turn);
        batchActivity?.SetTag("task_id", _taskId);
        batchActivity?.SetTag("call_count", calls.Count);
        batchActivity?.SetTag("denied_count", deniedCount);
        batchActivity?.SetTag("outcome", "completed");

        return new ToolExecutionResult(false, results.ToString().TrimEnd());
    }

    private sealed record BatchCallRequest(string Tool, string InputJson);

    private static bool TryParseBatchCalls(string json, out List<BatchCallRequest> calls, out string? error)
    {
        calls = [];
        error = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("calls", out var callsElement) ||
                callsElement.ValueKind != JsonValueKind.Array)
            {
                error = "Missing required property: calls";
                return false;
            }

            foreach (var callElement in callsElement.EnumerateArray())
            {
                if (!callElement.TryGetProperty("tool", out var toolElement) ||
                    toolElement.ValueKind != JsonValueKind.String)
                {
                    error = "Missing required property: calls[].tool";
                    return false;
                }

                var inputJson = callElement.TryGetProperty("input", out var inputElement)
                    ? inputElement.GetRawText()
                    : "{}";

                calls.Add(new BatchCallRequest(toolElement.GetString()!, inputJson));
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Malformed input JSON";
            return false;
        }
    }

    /// <summary>
    /// A batch rejected wholesale, before any member executed. Bypasses
    /// <see cref="RecordDenial"/>/<see cref="IToolCallInstrumentation.RecordDenied"/> in
    /// favor of the dedicated <c>wiki.batch.invocations_total</c>/<c>wiki.batch.rejected</c>
    /// signals — mirrors <see cref="RecordSearchPatternRejected"/>'s reasoning: this
    /// rejection reason has its own established signal pair, not the generic denial one.
    /// </summary>
    private ToolExecutionResult RecordBatchRejected(string reason, int callCount, int turn)
    {
        var record = new DeniedActionRecord(ToolRegistry.Batch, callCount.ToString(), callCount.ToString(), reason, turn);
        _denials.Add(record);

        var outcome = reason == "too_many_calls" ? "rejected_size" : "rejected_write";
        _instrumentation.RecordBatchInvocation(_taskId, outcome, turn);
        _instrumentation.LogBatchRejected(_taskId, reason, callCount, turn);

        return new ToolExecutionResult(true, DefaultDenialMessage(reason));
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

        // Copilot review (PR #199): canonicalize the path prefix exactly once and reuse the
        // result — a second, separate Canonicalize call reintroduced the same
        // unhandled-exception risk TryCanonicalize exists to close, for no benefit.
        var canonicalPathPrefix = string.Empty;
        if (hasPathPrefix && !TryCanonicalize(relativePathPrefix, out canonicalPathPrefix, out var searchCanonicalizationDenialReason))
        {
            var malformedDenial = RecordDenial(ToolRegistry.SearchFiles, relativePathPrefix, relativePathPrefix, searchCanonicalizationDenialReason!, turn);
            _instrumentation.RecordSearchInvocation(_taskId, "denied", matchesReturned: 0, filesScanned: 0, turn);
            return malformedDenial;
        }

        var searchRoot = hasPathPrefix ? canonicalPathPrefix : _repositoryRoot;

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

            var (fileTruncated, fileTimedOut) = ScanOneFile(candidateFile, regex, cap, stopwatch, matches, cancellationToken);
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

        if (!Directory.Exists(searchRoot))
        {
            return [];
        }

        // Directory.EnumerateFiles is lazily evaluated and can throw mid-walk — e.g. a
        // permission-denied subdirectory partway through the tree — so materialize it
        // defensively here rather than letting the exception propagate out of a `foreach`
        // in the caller. Whatever was already collected before the failure stays usable;
        // the walk just stops early (spec.md Edge Cases: a search "must not corrupt or
        // blow up the result").
        var candidates = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories))
            {
                candidates.Add(ResolvePhysicalPathInRepository(file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return candidates;
    }

    private (bool Truncated, bool TimedOut) ScanOneFile(
        string candidateFile, Regex regex, int cap, Stopwatch stopwatch, List<string> matches, CancellationToken cancellationToken)
    {
        var lineNumber = 0;
        try
        {
            foreach (var line in File.ReadLines(candidateFile))
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                _consideredPaths.Add(candidateFile);

                if (matches.Count >= cap)
                {
                    return (Truncated: true, TimedOut: false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable (binary, locked, permission-denied, deleted mid-scan) — skip
            // this file rather than fail the whole search.
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

        return new ToolExecutionResult(true, DefaultDenialMessage(reason));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private ToolExecutionResult RecordDenial(
        string action, string requestedTarget, string canonicalTarget, string reason, int turn, string? detail = null)
    {
        var record = new DeniedActionRecord(action, requestedTarget, canonicalTarget, reason, turn);
        _denials.Add(record);

        _instrumentation.RecordDenied(_taskId, action, requestedTarget, canonicalTarget, reason, turn);

        return new ToolExecutionResult(true, BuildDenialMessage(reason, detail));
    }

    // Issue #182: catalog_entry_malformed is an ADR-017 format rejection, not a
    // policy-scope denial — the write is exactly what the agent is supposed to be doing,
    // only the proposed content's shape was wrong. Telling the agent it is "outside the
    // safety policy" and to "continue with your remaining allowed work" describes a
    // different, unfixable situation and instructs it to abandon a write that is one edit
    // away from being accepted. Every other denial reason (scope, traversal,
    // create-only-exists, stale-read, coordination timeout, frontmatter preservation)
    // genuinely has nothing to correct, so it keeps the original message. 028-lint-at-scale
    // (US3, Clarifications 2026-08-27): log.md's own three format reasons
    // (log_entry_not_prepended/log_entry_malformed_heading/log_entry_missing_paragraph)
    // no longer reach this method at all — they never deny — so their hints were removed
    // as dead cases rather than left unreachable.
    private static string BuildDenialMessage(string reason, string? detail)
    {
        var hint = reason switch
        {
            "catalog_entry_malformed" =>
                "The write was rejected because a new index.md catalog line did not match the " +
                "required \"- [Title](path) — description — status\" shape, not because it is " +
                "out of scope. Fix that line's shape and reissue this write.",
            _ => (string?)null,
        };

        if (hint is null)
        {
            return DefaultDenialMessage(reason);
        }

        return detail is null
            ? $"denied: {reason}. {hint}"
            : $"denied: {reason}. {hint} Offending line: {detail}";
    }

    /// <summary>The generic denial message shared by every reason with no corrective hint.</summary>
    private static string DefaultDenialMessage(string reason) =>
        $"denied: {reason}. This action is outside the safety policy; continue with your remaining allowed work.";

    // D2 (027-host-stability, research.md): Linux's conventional MAXSYMLINKS/ELOOP limit,
    // reused as the recursion cap for ResolvePhysicalPathInRepository's chained-symlink walk.
    private const int MaxSymlinkResolutionHops = 40;

    /// <summary>
    /// D1/D2 (027-host-stability): thrown by <see cref="ResolvePhysicalPathInRepository(string, int)"/>
    /// when a chained symlink resolution exceeds <see cref="MaxSymlinkResolutionHops"/>.
    /// Caught only by <see cref="TryCanonicalize"/>, which turns it into a normal
    /// <c>symlink_loop</c> policy denial rather than letting it (or a stack overflow from
    /// an unbounded cyclic resolution) propagate out of the tool-call path.
    /// </summary>
    private sealed class SymlinkLoopExceededException : Exception
    {
    }

    /// <summary>
    /// D1 (027-host-stability): <see cref="Canonicalize"/> wraps .NET's <c>Path</c> APIs,
    /// which throw <see cref="ArgumentException"/> for an embedded NUL character rather
    /// than failing closed. This wrapper turns that (and a chained-symlink resolution that
    /// exceeds <see cref="MaxSymlinkResolutionHops"/>) into a normal policy-style denial —
    /// every dispatch method MUST canonicalize user-supplied path input through this method,
    /// never <see cref="Canonicalize"/> directly, so a malformed or adversarial path can
    /// never propagate as an unhandled exception out of the guarded-tool boundary
    /// (Constitution Principle V, host stability guarantee).
    /// </summary>
    private bool TryCanonicalize(string path, out string canonical, out string? denialReason)
    {
        try
        {
            canonical = Canonicalize(path);
            denialReason = null;
            return true;
        }
        catch (ArgumentException)
        {
            canonical = string.Empty;
            denialReason = "malformed_path";
            return false;
        }
        catch (SymlinkLoopExceededException)
        {
            canonical = string.Empty;
            denialReason = "symlink_loop";
            return false;
        }
    }

    /// <summary>
    /// D3 (027-host-stability): revalidates that <paramref name="canonical"/> still
    /// resolves to itself immediately before a mutating write/delete. Copilot review (PR
    /// #199): the raw re-resolution call this wraps can throw the same way the initial
    /// <see cref="Canonicalize"/> call can (a symlink swapped in during the TOCTOU window
    /// could itself form a loop past <see cref="MaxSymlinkResolutionHops"/>, or otherwise
    /// hit the <see cref="ArgumentException"/> case) — an exception thrown here, this close
    /// to the mutating call, is exactly the unhandled-exception failure mode this feature
    /// exists to close. Any resolution failure is treated as "does not match" (fails
    /// closed) rather than being allowed to propagate.
    /// </summary>
    private bool RevalidatesToSamePhysicalPath(string canonical)
    {
        try
        {
            return string.Equals(ResolvePhysicalPathInRepository(canonical), canonical, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (SymlinkLoopExceededException)
        {
            return false;
        }
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

    private string ResolvePhysicalPathInRepository(string fullPath) =>
        ResolvePhysicalPathInRepository(fullPath, MaxSymlinkResolutionHops);

    /// <summary>
    /// D2 (027-host-stability, research.md): a target reached through one segment's
    /// reparse point may itself be reached through further symlinks among its own
    /// remaining, unresolved path segments (e.g. a symlinked intermediate directory whose
    /// resolved target contains another symlink) — recursing the whole walk on the
    /// reconstructed path, instead of appending the tail literally and stopping, re-checks
    /// every remaining segment for reparse points too. <paramref name="hopsRemaining"/>
    /// caps the recursion so a symlink cycle denies (<see cref="SymlinkLoopExceededException"/>)
    /// rather than resolving forever.
    /// </summary>
    private string ResolvePhysicalPathInRepository(string fullPath, int hopsRemaining)
    {
        var canonical = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(_repositoryRoot, canonical);

        if (!IsRelativePathWithinRoot(relative))
        {
            return canonical;
        }

        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = _repositoryRoot;
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);

            if (!TryResolveLinkTarget(current, out var targetPath))
            {
                continue;
            }

            if (hopsRemaining <= 0)
            {
                throw new SymlinkLoopExceededException();
            }

            var reconstructed = targetPath;
            for (var j = i + 1; j < parts.Length; j++)
            {
                reconstructed = Path.Combine(reconstructed, parts[j]);
            }

            return ResolvePhysicalPathInRepository(reconstructed, hopsRemaining - 1);
        }

        return Path.GetFullPath(current);
    }

    private static bool IsRelativePathWithinRoot(string relative) =>
        !Path.IsPathRooted(relative) &&
        !relative.Equals("..", StringComparison.Ordinal) &&
        !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool TryResolveLinkTarget(string path, out string resolvedTarget)
    {
        resolvedTarget = string.Empty;
        var isDirectory = Directory.Exists(path);

        if (!isDirectory && !File.Exists(path))
        {
            return false;
        }

        try
        {
            FileSystemInfo info = isDirectory
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
