using Grimoire.Domain.Guardrails;
using System.Security.Cryptography;
using System.Text;

namespace Grimoire.AgentRuntime.Guardrails.Coordination;

/// <summary>
/// The outcome of <see cref="SharedFileWriteGuard.EvaluateWriteAsync"/>. On allow, the
/// caller performs the existing atomic write while still holding <see cref="LockHandle"/>,
/// then calls <see cref="SharedFileWriteGuard.OnWriteCommitted"/> before disposing it
/// (contract §3) — the lock protects the whole check-then-write critical section, not
/// just the check. On denial, the lock has already been released internally; there is
/// nothing left for the caller to hold or dispose.
/// </summary>
public sealed record WriteGuardDecision(bool IsAllowed, string? DenialReason, IDisposable? LockHandle)
{
    public static WriteGuardDecision Allowed(IDisposable lockHandle) => new(true, null, lockHandle);

    /// <param name="reason">
    /// One of: <c>create_only_target_exists</c>, <c>write_conflict_stale_read</c>,
    /// <c>write_coordination_timeout</c>, or, since ADR-016 (013-lint-agent):
    /// <c>frontmatter_only_target_missing</c>, <c>frontmatter_only_malformed_document</c>,
    /// <c>frontmatter_only_body_changed</c>.
    /// </param>
    public static WriteGuardDecision Denied(string reason) => new(false, reason, null);
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
    private readonly string _writeLocksDir;
    private readonly TimeSpan _backoffCap;
    private readonly Dictionary<string, string> _readHashes = new(StringComparer.Ordinal);

    public SharedFileWriteGuard(string writeLocksDir, TimeSpan? backoffCap = null)
    {
        _writeLocksDir = writeLocksDir;
        _backoffCap = backoffCap ?? CrossProcessFileLock.DefaultBackoffCap;
    }

    /// <summary>Records the content hash of a successful <c>read_file</c>, for later compare-and-swap.</summary>
    public void OnReadFile(string canonicalPath, string content)
        => _readHashes[canonicalPath] = ComputeHash(content);

    /// <summary>
    /// Pre-ADR-016 boolean-mode overload, retained for source compatibility with every
    /// existing call site and test written against the two-mode (create-only/read-write)
    /// shape. Delegates to <see cref="EvaluateWriteAsync(string, WriteMode, string, CancellationToken)"/>
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
    /// The write call's proposed new content. Only inspected for
    /// <see cref="WriteMode.FrontmatterOnly"/> (ADR-016) — <see cref="WriteMode.ReadWrite"/>
    /// and <see cref="WriteMode.CreateOnly"/> ignore it entirely, matching their pre-ADR-016
    /// behavior exactly.
    /// </param>
    public async Task<WriteGuardDecision> EvaluateWriteAsync(
        string canonicalPath, WriteMode mode, string proposedContent, CancellationToken cancellationToken)
    {
        var handle = await CrossProcessFileLock.TryAcquireAsync(
            _writeLocksDir, canonicalPath, _backoffCap, cancellationToken);

        if (handle is null)
        {
            return WriteGuardDecision.Denied("write_coordination_timeout");
        }

        var exists = File.Exists(canonicalPath);

        if (mode == WriteMode.CreateOnly)
        {
            if (exists)
            {
                handle.Dispose();
                return WriteGuardDecision.Denied("create_only_target_exists");
            }

            return WriteGuardDecision.Allowed(handle);
        }

        // ADR-016: a frontmatter-only write always targets a page that already exists —
        // Lint never creates pages, so a missing target is denied before any content check.
        if (mode == WriteMode.FrontmatterOnly && !exists)
        {
            handle.Dispose();
            return WriteGuardDecision.Denied("frontmatter_only_target_missing");
        }

        byte[]? currentBytes = null;
        if (exists)
        {
            currentBytes = await File.ReadAllBytesAsync(canonicalPath, cancellationToken);
            var currentHash = ComputeHash(currentBytes);
            var expectedHash = _readHashes.GetValueOrDefault(canonicalPath);

            if (expectedHash is null || !string.Equals(expectedHash, currentHash, StringComparison.Ordinal))
            {
                handle.Dispose();
                return WriteGuardDecision.Denied("write_conflict_stale_read");
            }
        }

        // ADR-016: the frontmatter-only body-preservation check composes with, not
        // replaces, the compare-and-swap check above — both must pass. `exists` and
        // `currentBytes` are guaranteed non-null here (denied above otherwise).
        if (mode == WriteMode.FrontmatterOnly)
        {
            var currentContent = Encoding.UTF8.GetString(currentBytes!);

            if (!TrySplitFrontmatter(currentContent, out var currentBody) ||
                !TrySplitFrontmatter(proposedContent, out var proposedBody))
            {
                handle.Dispose();
                return WriteGuardDecision.Denied("frontmatter_only_malformed_document");
            }

            if (!string.Equals(currentBody, proposedBody, StringComparison.Ordinal))
            {
                handle.Dispose();
                return WriteGuardDecision.Denied("frontmatter_only_body_changed");
            }
        }

        return WriteGuardDecision.Allowed(handle);
    }

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
