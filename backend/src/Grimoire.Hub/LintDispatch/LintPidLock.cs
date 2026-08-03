namespace Grimoire.Hub.LintDispatch;

/// <summary>
/// Exclusive OS-level file lock on the single, fixed <c>lint.pid</c> file
/// (018-hub-cli-commands ADR-020, research.md D1a), held by
/// <see cref="LintRunCoordinator.TriggerAsync"/> for a Lint Run's full duration across
/// BOTH entry paths (HTTP and CLI) — so a second process attempting to trigger a run
/// while one is already active is detected the same way the in-process
/// <c>_slot</c> semaphore already detects same-process contention.
///
/// Unlike <c>Grimoire.AgentRuntime.Guardrails.Coordination.CrossProcessFileLock</c>
/// (ADR-015), which locks one of many per-target files named by a SHA-256 hash of a
/// "canonical target path" argument, this lock always targets exactly one fixed path
/// (<c>ResolvedGrimoirePaths.LintPidPath</c>) resolved once at startup — a dedicated,
/// minimal, single-attempt lock (no retry/backoff loop) is a cleaner fit for this shape
/// than reusing the hashed-multi-target one. "Single attempt" also matches the caller's
/// need: an immediate busy/not-busy answer, mirroring the in-process semaphore's
/// non-blocking <c>WaitAsync(0)</c> acquire — the CLI/HTTP caller wants "is a run already
/// active" answered now, not after waiting out a backoff window.
/// </summary>
public sealed class LintPidLock : IDisposable
{
    private readonly FileStream _stream;
    private bool _disposed;

    private LintPidLock(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Attempts to acquire the exclusive lock on <paramref name="lintPidPath"/>
    /// immediately — no retry, no backoff. Returns <see langword="null"/> if another
    /// process (or another in-process holder) currently holds it; never throws for that
    /// case.
    /// </summary>
    public static LintPidLock? TryAcquire(string lintPidPath)
    {
        var directory = Path.GetDirectoryName(lintPidPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            var stream = new FileStream(lintPidPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new LintPidLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
    }
}
