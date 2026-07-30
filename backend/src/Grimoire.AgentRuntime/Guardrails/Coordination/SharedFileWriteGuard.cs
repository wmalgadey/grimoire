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

    /// <param name="reason">One of: <c>create_only_target_exists</c>, <c>write_conflict_stale_read</c>, <c>write_coordination_timeout</c>.</param>
    public static WriteGuardDecision Denied(string reason) => new(false, reason, null);
}

/// <summary>
/// Cross-process write coordination (ADR-015), one instance per agent-process run — same
/// lifecycle as <see cref="WriteJournal"/>. Two responsibilities, both pure harness
/// mechanics (Constitution Principle V: no wiki-content judgment):
/// <list type="number">
/// <item>Read tracking: records the SHA-256 of every file this run reads via
/// <see cref="OnReadFile"/>, keyed by canonical path.</item>
/// <item>Guarded write: <see cref="EvaluateWriteAsync"/> acquires a per-target
/// <see cref="CrossProcessFileLock"/>, then — while still holding it — applies the
/// create-only existence check or the read-then-write compare-and-swap check before
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
    /// Acquires the per-target lock and evaluates the create-only/compare-and-swap
    /// decision while holding it (contract §3, data-model.md state transition). On allow,
    /// the returned <see cref="WriteGuardDecision.LockHandle"/> MUST be disposed by the
    /// caller after the write completes (successfully or not) — via
    /// <see cref="OnWriteCommitted"/> then <see cref="IDisposable.Dispose"/>, both inside
    /// the caller's <c>finally</c>.
    /// </summary>
    public async Task<WriteGuardDecision> EvaluateWriteAsync(
        string canonicalPath, bool isCreateOnly, CancellationToken cancellationToken)
    {
        var handle = await CrossProcessFileLock.TryAcquireAsync(
            _writeLocksDir, canonicalPath, _backoffCap, cancellationToken);

        if (handle is null)
        {
            return WriteGuardDecision.Denied("write_coordination_timeout");
        }

        var exists = File.Exists(canonicalPath);

        if (isCreateOnly)
        {
            if (exists)
            {
                handle.Dispose();
                return WriteGuardDecision.Denied("create_only_target_exists");
            }

            return WriteGuardDecision.Allowed(handle);
        }

        if (exists)
        {
            var currentHash = ComputeHash(await File.ReadAllBytesAsync(canonicalPath, cancellationToken));
            var expectedHash = _readHashes.GetValueOrDefault(canonicalPath);

            if (expectedHash is null || !string.Equals(expectedHash, currentHash, StringComparison.Ordinal))
            {
                handle.Dispose();
                return WriteGuardDecision.Denied("write_conflict_stale_read");
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

    private static string ComputeHash(string content) => ComputeHash(Encoding.UTF8.GetBytes(content));

    private static string ComputeHash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
