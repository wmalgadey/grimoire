using Grimoire.Domain.Guardrails;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Grimoire.AgentRuntime.Guardrails.Coordination;

/// <summary>
/// The outcome of <see cref="SharedFileWriteGuard.EvaluateWriteAsync"/>. On allow, the
/// caller performs the existing atomic write while still holding <see cref="LockHandle"/>,
/// then calls <see cref="SharedFileWriteGuard.OnWriteCommitted"/> before disposing it
/// (contract §3) — the lock protects the whole check-then-write critical section, not
/// just the check. On denial, the lock has already been released internally; there is
/// nothing left for the caller to hold or dispose.
/// </summary>
public sealed record WriteGuardDecision(
    bool IsAllowed,
    string? DenialReason,
    IDisposable? LockHandle,
    string? Detail = null,
    // 028-lint-at-scale (US3, FR-010): the caller's own `content` for `mode: "replace"`
    // (equal to what the caller already had); the assembled `entry + currentContent` for
    // `mode: "prepend"`, read fresh under the lock — the caller writes exactly this to
    // disk (data-model.md "Prepend-mode write assembly"). Meaningless on a denied decision.
    string? ResolvedContent = null,
    // 028-lint-at-scale (US3, FR-011/FR-016, Clarifications 2026-08-27, FSI-3): the
    // activity-log format/ordering check's result on an ALLOWED write — never a denial
    // reason. Null when the write is not to log.md, or conforms; one or more of
    // log_entry_not_prepended/log_entry_malformed_heading/log_entry_missing_paragraph
    // otherwise (research.md R15).
    IReadOnlyList<string>? FormatDeviationReasons = null)
{
    public static WriteGuardDecision Allowed(
        IDisposable lockHandle, string? resolvedContent = null, IReadOnlyList<string>? formatDeviationReasons = null)
        => new(true, null, lockHandle, ResolvedContent: resolvedContent, FormatDeviationReasons: formatDeviationReasons);

    /// <param name="reason">
    /// One of: <c>create_only_target_exists</c>, <c>write_conflict_stale_read</c>,
    /// <c>write_coordination_timeout</c>, or, since ADR-016 (013-lint-agent):
    /// <c>frontmatter_only_target_missing</c>, <c>frontmatter_only_malformed_document</c>,
    /// <c>frontmatter_only_body_changed</c>; or (US4) <c>catalog_entry_malformed</c>.
    /// The three log.md format/ordering reasons (<c>log_entry_not_prepended</c>,
    /// <c>log_entry_malformed_heading</c>, <c>log_entry_missing_paragraph</c>) are never
    /// passed here since 028-lint-at-scale (Clarifications 2026-08-27) — see
    /// <see cref="FormatDeviationReasons"/> instead.
    /// </param>
    /// <param name="detail">
    /// Issue #182: the locating detail the format check already has at the point of
    /// denial — currently only <c>catalog_entry_malformed</c> carries one (the offending
    /// <c>- [</c> line). <c>null</c> for every other reason.
    /// </param>
    public static WriteGuardDecision Denied(string reason, string? detail = null) => new(false, reason, null, detail);
}

/// <summary>
/// Cross-process write coordination (ADR-015, extended by ADR-016), one instance per
/// agent-process run — same lifecycle as <see cref="WriteJournal"/>. Two responsibilities,
/// both pure harness mechanics (Constitution Principle V: no wiki-content judgment):
/// <list type="number">
/// <item>Read tracking: records the SHA-256 of every file this run reads via
/// <see cref="OnReadFile"/>, keyed by canonical path.</item>
/// <item>Guarded write: <see cref="EvaluateWriteAsync"/> acquires a per-target
/// <see cref="CrossProcessFileLock"/>, then — while still holding it — applies the
/// create-only existence check, the read-then-write compare-and-swap check, and (ADR-016,
/// <see cref="WriteMode.FrontmatterOnly"/>) the frontmatter/body-preservation check before
/// allowing the caller to proceed with the actual write.</item>
/// </list>
/// Constructed only from <see cref="GuardedToolExecutor"/> (or other types within
/// <c>Grimoire.AgentRuntime.Guardrails</c>), enforced by
/// <c>GuardrailsCoordinationContainmentRuleTests</c>.
/// </summary>
public sealed class SharedFileWriteGuard
{
    // ADR-017 (014-wiki-storage-restructure), amended by ADR-028 (025-agent-owned-log):
    // the prepended head's first non-blank line must be a "[DATE] TYPE | SUMMARY" heading
    // — the pattern itself is unchanged (FR-008), only which slice it is applied to.
    // contracts/activity-log-write-contract.md §3 R2.
    private static readonly Regex LogHeadingPattern =
        new(@"^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$", RegexOptions.Compiled);

    // ADR-017 (014-wiki-storage-restructure, US4): a newly added index.md catalog line
    // must be "[Title](path) — description — status" —
    // contracts/log-and-catalog-entry-format.md.
    private static readonly Regex CatalogEntryPattern =
        new(@"^- \[.+\]\(.+\) — .+ — .+$", RegexOptions.Compiled);

    private readonly string _writeLocksDir;
    private readonly TimeSpan _backoffCap;
    private readonly string? _logPath;
    private readonly string? _indexPath;
    private readonly ActivitySource? _activitySource;
    private readonly Dictionary<string, string> _readHashes = new(StringComparer.Ordinal);

    /// <param name="logPath">
    /// ADR-017 (014-wiki-storage-restructure): the canonicalized <c>log.md</c> path this
    /// run's guarded writes are evaluated against — <c>null</c> (the default) disables
    /// the format-validation step entirely, matching every caller written before this
    /// feature. Must already be canonicalized the same way <paramref name="logPath"/>-argument
    /// callers canonicalize every other guarded-write target, so ordinal equality against
    /// <see cref="EvaluateWriteAsync(string, Grimoire.Domain.Guardrails.WriteMode, string, CancellationToken, bool)"/>'s
    /// <c>canonicalPath</c> is reliable.
    /// </param>
    /// <param name="indexPath">
    /// ADR-017 (014-wiki-storage-restructure, US4): the canonicalized <c>index.md</c>
    /// path this run's guarded writes are evaluated against — <c>null</c> (the default)
    /// disables the catalog-entry format-validation step entirely, matching every caller
    /// written before US4. Must already be canonicalized the same way every other
    /// guarded-write target is, so ordinal equality against
    /// <see cref="EvaluateWriteAsync(string, Grimoire.Domain.Guardrails.WriteMode, string, CancellationToken, bool)"/>'s
    /// <c>canonicalPath</c> is reliable.
    /// </param>
    /// <param name="activitySource">
    /// The calling agent process's own frozen <see cref="ActivitySource"/> (ADR-005/
    /// ADR-013) — used only to start the <c>guardrails.format_validate</c> span around the
    /// format-validation step (for both <paramref name="logPath"/> and
    /// <paramref name="indexPath"/>, distinguished by the span's <c>target</c> attribute).
    /// <c>null</c> (the default, and every pre-ADR-017 caller) means the check still runs
    /// but emits no span — hermetic tests that construct this type directly without an
    /// OTel listener are unaffected.
    /// </param>
    public SharedFileWriteGuard(
        string writeLocksDir,
        TimeSpan? backoffCap = null,
        string? logPath = null,
        string? indexPath = null,
        ActivitySource? activitySource = null)
    {
        _writeLocksDir = writeLocksDir;
        _backoffCap = backoffCap ?? CrossProcessFileLock.DefaultBackoffCap;
        _logPath = logPath;
        _indexPath = indexPath;
        _activitySource = activitySource;
    }

    /// <summary>Records the content hash of a successful <c>read_file</c>, for later compare-and-swap.</summary>
    public void OnReadFile(string canonicalPath, string content)
        => _readHashes[canonicalPath] = ComputeHash(content);

    /// <summary>
    /// Pre-ADR-016 boolean-mode overload, retained for source compatibility with every
    /// existing call site and test written against the two-mode (create-only/read-write)
    /// shape. Delegates to <see cref="EvaluateWriteAsync(string, WriteMode, string, CancellationToken, bool)"/>
    /// with an empty proposed content — never dereferenced for these two modes, since the
    /// frontmatter/body check only runs for <see cref="WriteMode.FrontmatterOnly"/>.
    /// </summary>
    public Task<WriteGuardDecision> EvaluateWriteAsync(
        string canonicalPath, bool isCreateOnly, CancellationToken cancellationToken)
        => EvaluateWriteAsync(
            canonicalPath,
            isCreateOnly ? WriteMode.CreateOnly : WriteMode.ReadWrite,
            proposedContent: string.Empty,
            cancellationToken);

    /// <summary>
    /// Acquires the per-target lock and evaluates the create-only/compare-and-swap/
    /// frontmatter-only decision while holding it (contract §3, data-model.md state
    /// transition; ADR-016 for <see cref="WriteMode.FrontmatterOnly"/>). On allow, the
    /// returned <see cref="WriteGuardDecision.LockHandle"/> MUST be disposed by the caller
    /// after the write completes (successfully or not) — via <see cref="OnWriteCommitted"/>
    /// then <see cref="IDisposable.Dispose"/>, both inside the caller's <c>finally</c>.
    /// </summary>
    /// <param name="proposedContent">
    /// The write call's proposed new content for <c>mode: "replace"</c> — only inspected
    /// for <see cref="WriteMode.FrontmatterOnly"/> (ADR-016) — <see cref="WriteMode.ReadWrite"/>
    /// and <see cref="WriteMode.CreateOnly"/> ignore it entirely, matching their pre-ADR-016
    /// behavior exactly. For <paramref name="isPrepend"/>, this is the new entry only — see
    /// its own doc comment.
    /// </param>
    /// <param name="isPrepend">
    /// 028-lint-at-scale (US3, FR-010): the <c>write_file</c> call's own <c>mode</c> was
    /// <c>"prepend"</c> — <paramref name="proposedContent"/> is the new entry alone, and
    /// this method reads the target's current content itself, fresh, under the lock (no
    /// <see cref="OnReadFile"/> baseline, no compare-and-swap — research.md R8: there is no
    /// staleness scenario for a prepend to be stale against), assembles
    /// <c>entry + currentContent</c>, and returns it as
    /// <see cref="WriteGuardDecision.ResolvedContent"/>. Bypasses the create-only/CAS/
    /// frontmatter-only checks entirely — a distinct write shape, not a mode combination.
    /// Default <c>false</c> so every pre-existing positional call site is unaffected.
    /// </param>
    public async Task<WriteGuardDecision> EvaluateWriteAsync(
        string canonicalPath, WriteMode mode, string proposedContent, CancellationToken cancellationToken, bool isPrepend = false)
    {
        var handle = await CrossProcessFileLock.TryAcquireAsync(
            _writeLocksDir, canonicalPath, _backoffCap, cancellationToken);

        if (handle is null)
        {
            return WriteGuardDecision.Denied("write_coordination_timeout");
        }

        if (isPrepend)
        {
            var currentContent = File.Exists(canonicalPath)
                ? await File.ReadAllTextAsync(canonicalPath, cancellationToken)
                : string.Empty;
            var assembled = proposedContent + currentContent;
            var deviations = EvaluateLogFormatDeviations(canonicalPath, currentContent, assembled);

            return WriteGuardDecision.Allowed(
                handle, resolvedContent: assembled, formatDeviationReasons: deviations.Count > 0 ? deviations : null);
        }

        var exists = File.Exists(canonicalPath);

        if (mode == WriteMode.CreateOnly)
        {
            if (exists)
            {
                handle.Dispose();
                return WriteGuardDecision.Denied("create_only_target_exists");
            }

            return WriteGuardDecision.Allowed(handle, resolvedContent: proposedContent);
        }

        var (denialReason, denialDetail, formatDeviations) =
            await EvaluateExistingTargetChecksAsync(canonicalPath, mode, proposedContent, exists, cancellationToken);
        if (denialReason is not null)
        {
            handle.Dispose();
            return WriteGuardDecision.Denied(denialReason, denialDetail);
        }

        return WriteGuardDecision.Allowed(
            handle, resolvedContent: proposedContent, formatDeviationReasons: formatDeviations.Count > 0 ? formatDeviations : null);
    }

    /// <summary>
    /// The read-write/frontmatter-only half of <see cref="EvaluateWriteAsync(string, WriteMode, string, CancellationToken, bool)"/>
    /// — every check that only applies once create-only mode has been ruled out: the
    /// frontmatter-only missing-target check, the compare-and-swap read-hash check
    /// (ADR-015), the frontmatter/body-preservation check (ADR-016), and index.md's
    /// catalog-entry format check (ADR-017) — all of which still deny. log.md's own
    /// format/ordering check runs here too but never denies (028-lint-at-scale US3,
    /// Clarifications 2026-08-27, FSI-3) — its result rides back as
    /// <c>FormatDeviations</c> instead. Returns the first denial reason encountered (or
    /// <c>null</c> once every denying check has passed) alongside whatever log-format
    /// deviations were found — the caller still owns disposing the lock handle either way.
    /// </summary>
    private async Task<(string? Reason, string? Detail, IReadOnlyList<string> FormatDeviations)> EvaluateExistingTargetChecksAsync(
        string canonicalPath, WriteMode mode, string proposedContent, bool exists, CancellationToken cancellationToken)
    {
        // ADR-016: a frontmatter-only write always targets a page that already exists —
        // Lint never creates pages, so a missing target is denied before any content check.
        if (mode == WriteMode.FrontmatterOnly && !exists)
        {
            return ("frontmatter_only_target_missing", null, []);
        }

        byte[]? currentBytes = null;
        if (exists)
        {
            currentBytes = await File.ReadAllBytesAsync(canonicalPath, cancellationToken);

            var casDenialReason = EvaluateCompareAndSwap(canonicalPath, currentBytes);
            if (casDenialReason is not null)
            {
                return (casDenialReason, null, []);
            }
        }

        // ADR-016: the frontmatter-only body-preservation check composes with, not
        // replaces, the compare-and-swap check above — both must pass. `currentBytes` is
        // guaranteed non-null here (denied above otherwise).
        if (mode == WriteMode.FrontmatterOnly)
        {
            var frontmatterDenialReason = EvaluateFrontmatterPreservation(currentBytes!, proposedContent);
            if (frontmatterDenialReason is not null)
            {
                return (frontmatterDenialReason, null, []);
            }
        }

        var currentContent = exists ? Encoding.UTF8.GetString(currentBytes!) : string.Empty;

        // ADR-017 (014-wiki-storage-restructure): log.md's format/ordering check, gated on
        // the canonical target being log.md, run after every existence/CAS/WriteMode check
        // above and before the write is committed (contract §3 order) — composes with,
        // never replaces, the checks above. Never denies (FSI-3): its result is a
        // deviation report, not a gate.
        var logDeviations = EvaluateLogFormatDeviations(canonicalPath, currentContent, proposedContent);

        // index.md's catalog-entry check — unchanged, still denies. Issue #182: it returns
        // a locating detail alongside its reason (the offending `- [` line) so the harness
        // can tell the agent exactly what to fix instead of just naming the reason code.
        var catalogResult = EvaluateFormatIfTarget(canonicalPath, _indexPath, "index", currentContent, proposedContent, ValidateCatalogEntryFormat);
        return catalogResult.Reason is not null
            ? (catalogResult.Reason, catalogResult.Detail, logDeviations)
            : (null, null, logDeviations);
    }

    private string? EvaluateCompareAndSwap(string canonicalPath, byte[] currentBytes)
    {
        var currentHash = ComputeHash(currentBytes);
        var expectedHash = _readHashes.GetValueOrDefault(canonicalPath);

        return expectedHash is null || !string.Equals(expectedHash, currentHash, StringComparison.Ordinal)
            ? "write_conflict_stale_read"
            : null;
    }

    private static string? EvaluateFrontmatterPreservation(byte[] currentBytes, string proposedContent)
    {
        var currentContent = Encoding.UTF8.GetString(currentBytes);

        if (!TrySplitFrontmatter(currentContent, out var currentBody) ||
            !TrySplitFrontmatter(proposedContent, out var proposedBody))
        {
            return "frontmatter_only_malformed_document";
        }

        return string.Equals(currentBody, proposedBody, StringComparison.Ordinal)
            ? null
            : "frontmatter_only_body_changed";
    }

    /// <summary>
    /// Runs <paramref name="validate"/> and emits the <c>guardrails.format_validate</c>
    /// span when <paramref name="canonicalPath"/> is <paramref name="targetPath"/> — a
    /// no-op returning <c>null</c> otherwise. Used only for index.md's catalog-entry check
    /// (still denies) — log.md's own format check has its own non-denying counterpart,
    /// <see cref="EvaluateLogFormatDeviations"/> (028-lint-at-scale US3, FSI-3), since this
    /// helper's shape assumes a single denial-or-allow outcome that no longer fits log.md.
    /// </summary>
    private (string? Reason, string? Detail) EvaluateFormatIfTarget(
        string canonicalPath, string? targetPath, string targetLabel,
        string currentContent, string proposedContent, Func<string, string, (string? Reason, string? Detail)> validate)
    {
        if (targetPath is null || !string.Equals(canonicalPath, targetPath, StringComparison.Ordinal))
        {
            return (null, null);
        }

        var (formatDenialReason, detail) = validate(currentContent, proposedContent);

        using var formatSpan = _activitySource?.StartActivity("guardrails.format_validate");
        formatSpan?.SetTag("path", canonicalPath);
        formatSpan?.SetTag("target", targetLabel);
        formatSpan?.SetTag("outcome", formatDenialReason is null ? "allowed" : "denied");
        if (formatDenialReason is not null)
        {
            formatSpan?.SetTag("reason", formatDenialReason);
        }

        return (formatDenialReason, detail);
    }

    /// <summary>
    /// Runs <see cref="ComputeLogFormatDeviations"/> when <paramref name="canonicalPath"/>
    /// is <see cref="_logPath"/> (a no-op returning an empty list otherwise) and emits the
    /// <c>guardrails.format_validate</c> span — <c>outcome</c> <c>conforming</c>/<c>deviated</c>
    /// (028-lint-at-scale US3, T030: retargeted from <c>allowed</c>/<c>denied</c>, since this
    /// check no longer gates the write). Shared by the replace-mode path
    /// (<see cref="EvaluateExistingTargetChecksAsync"/>, where <paramref name="proposedContent"/>
    /// is the full file) and the prepend-mode path (<see cref="EvaluateWriteAsync(string, WriteMode, string, CancellationToken, bool)"/>,
    /// where it is <c>entry + currentContent</c> assembled fresh under the lock).
    /// </summary>
    private IReadOnlyList<string> EvaluateLogFormatDeviations(string canonicalPath, string currentContent, string proposedContent)
    {
        if (_logPath is null || !string.Equals(canonicalPath, _logPath, StringComparison.Ordinal))
        {
            return [];
        }

        var deviations = ComputeLogFormatDeviations(currentContent, proposedContent);

        using var formatSpan = _activitySource?.StartActivity("guardrails.format_validate");
        formatSpan?.SetTag("path", canonicalPath);
        formatSpan?.SetTag("target", "log");
        formatSpan?.SetTag("outcome", deviations.Count == 0 ? "conforming" : "deviated");
        if (deviations.Count > 0)
        {
            formatSpan?.SetTag("reason", string.Join(",", deviations));
        }

        return deviations;
    }

    /// <summary>
    /// ADR-017, amended by ADR-028 (025-agent-owned-log) and 028-lint-at-scale
    /// (Clarifications 2026-08-27, FSI-3): the log.md structural shape check — prepend
    /// order (FR-003, FR-004), then heading pattern, then a following non-blank paragraph
    /// (contracts/log-prepend-write.md). The current content must be an unchanged
    /// <em>suffix</em> of the proposed content, so a new entry lands at the top and every
    /// existing entry survives byte-for-byte below it; the checks then run over the
    /// prepended <em>head</em>. When the ordering itself is wrong there is no well-defined
    /// head to subtract, so the heading/paragraph check runs over the whole proposed
    /// content instead — a malformed heading is still surfaced even when the ordering is
    /// also wrong (both reasons can apply to the same write). Empty current content
    /// (missing or zero-length file) satisfies the ordering rule trivially, so the first
    /// agent write creates the file (FR-010).
    ///
    /// Pure string/regex operation over content already resident in memory (no I/O, no
    /// judgment about whether a given SUMMARY/paragraph is good — Constitution Principle
    /// V). Returns every applicable reason code — empty if the proposed content conforms —
    /// never a single early-return reason, since a non-conforming write commits regardless
    /// and every deviation it carries is worth reporting (data-model.md "Format/ordering
    /// deviation signal": "one write may carry more than one").
    /// </summary>
    private static IReadOnlyList<string> ComputeLogFormatDeviations(string currentContent, string proposedContent)
    {
        var reasons = new List<string>();

        string head;
        if (!proposedContent.EndsWith(currentContent, StringComparison.Ordinal))
        {
            reasons.Add("log_entry_not_prepended");
            head = proposedContent;
        }
        else
        {
            head = proposedContent[..^currentContent.Length];
        }

        var headingOrParagraphReason = ValidateLogHeadShape(head);
        if (headingOrParagraphReason is not null)
        {
            reasons.Add(headingOrParagraphReason);
        }

        return reasons;
    }

    private static string? ValidateLogHeadShape(string head)
    {
        var lines = head.Split('\n');

        var i = 0;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
        {
            i++;
        }

        if (i >= lines.Length || !LogHeadingPattern.IsMatch(lines[i].TrimEnd('\r')))
        {
            return "log_entry_malformed_heading";
        }

        i++;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
        {
            i++;
        }

        return i >= lines.Length ? "log_entry_missing_paragraph" : null;
    }

    /// <summary>
    /// ADR-017 (014-wiki-storage-restructure, US4): the index.md catalog-entry shape
    /// check, scoped to newly added lines only (contract §"index.md Catalog Entry"; FR-012).
    /// Computes the set of <c>- [</c>-led lines present in <paramref name="proposedContent"/>
    /// but absent, byte-for-byte, from <paramref name="currentContent"/>, and denies unless
    /// every one of them matches the link-description-status shape. Lines that already
    /// existed verbatim, and any line not starting with <c>- [</c> (section headings,
    /// blank lines), are never checked — pure string/regex operation over content already
    /// resident in memory, no judgment about whether a given description is good
    /// (Constitution Principle V). Returns the denial reason and the offending line
    /// (issue #182: the harness's own locating detail, so a denial can point at exactly
    /// what to fix), or <c>(null, null)</c> if every new catalog line conforms.
    /// </summary>
    internal static (string? Reason, string? Detail) ValidateCatalogEntryFormat(string currentContent, string proposedContent)
    {
        var currentLines = new HashSet<string>(SplitLines(currentContent), StringComparer.Ordinal);

        foreach (var line in SplitLines(proposedContent))
        {
            if (!line.StartsWith("- [", StringComparison.Ordinal))
            {
                continue;
            }

            if (currentLines.Contains(line))
            {
                continue;
            }

            if (!CatalogEntryPattern.IsMatch(line))
            {
                return ("catalog_entry_malformed", line);
            }
        }

        return (null, null);
    }

    private static IEnumerable<string> SplitLines(string content)
        => content.Split('\n').Select(line => line.TrimEnd('\r'));

    /// <summary>
    /// Updates the read-hash baseline for <paramref name="canonicalPath"/> to the content
    /// this run just wrote — MUST be called after a successful write and before the lock
    /// is released, so a run's own subsequent write to the same path (e.g. a page it just
    /// created) always succeeds without needing a prior <c>read_file</c>.
    /// </summary>
    public void OnWriteCommitted(string canonicalPath, string content)
        => _readHashes[canonicalPath] = ComputeHash(content);

    /// <summary>
    /// ADR-016 (013-lint-agent): splits <paramref name="content"/> at its closing
    /// frontmatter delimiter — the first line must be exactly <c>---</c> (the opening
    /// delimiter), and the next line that is exactly <c>---</c> closes the block;
    /// <paramref name="body"/> is everything after that closing line's terminator
    /// (including no leading newline of its own, so an unchanged body compares
    /// byte-for-byte via ordinal string equality). Returns <c>false</c> — and leaves
    /// <paramref name="body"/> empty — if <paramref name="content"/> does not open with a
    /// <c>---</c> line or has no subsequent <c>---</c> line: fails closed, since a
    /// document this check cannot parse is never assumed to have an unchanged body.
    /// Pure string operation, no I/O, no YAML parse (research.md R2) — a mechanical
    /// structure check only, never a judgment about the frontmatter's content
    /// (Constitution Principle V).
    /// </summary>
    private static bool TrySplitFrontmatter(string content, out string body)
    {
        body = string.Empty;

        var firstLineEnd = content.IndexOf('\n');
        if (firstLineEnd < 0 || content[..firstLineEnd].TrimEnd('\r') != "---")
        {
            return false;
        }

        var searchStart = firstLineEnd + 1;
        while (searchStart <= content.Length)
        {
            var nextLineEnd = content.IndexOf('\n', searchStart);
            var lineEndExclusive = nextLineEnd < 0 ? content.Length : nextLineEnd;
            var line = content[searchStart..lineEndExclusive].TrimEnd('\r');

            if (line == "---")
            {
                body = nextLineEnd < 0 ? string.Empty : content[(nextLineEnd + 1)..];
                return true;
            }

            if (nextLineEnd < 0)
            {
                break;
            }

            searchStart = nextLineEnd + 1;
        }

        return false;
    }

    private static string ComputeHash(string content) => ComputeHash(Encoding.UTF8.GetBytes(content));

    private static string ComputeHash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
