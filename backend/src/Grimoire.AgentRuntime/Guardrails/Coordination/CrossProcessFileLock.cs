using System.Security.Cryptography;
using System.Text;

namespace Grimoire.AgentRuntime.Guardrails.Coordination;

/// <summary>
/// An OS-level exclusive lock on a per-target lock file (ADR-015), held for the duration
/// of one guarded write's existence/hash-check plus atomic rename. Acquired via
/// <see cref="TryAcquireAsync"/>; release with <see cref="Dispose"/> — always in the
/// caller's <c>finally</c>, including on cancellation, so an interrupted run cannot wedge
/// a target for later runs. An OS file lock also releases automatically if the holding
/// process is killed outright, so a crashed run cannot wedge it permanently either.
/// </summary>
public sealed class CrossProcessFileLock : IDisposable
{
    /// <summary>Default acquisition backoff cap (contract §3, plan.md Performance Goals).</summary>
    public static readonly TimeSpan DefaultBackoffCap = TimeSpan.FromMilliseconds(5000);

    private static readonly TimeSpan InitialBackoffDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MaxBackoffDelay = TimeSpan.FromMilliseconds(200);

    private readonly FileStream _stream;
    private bool _disposed;

    private CrossProcessFileLock(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Attempts to acquire the exclusive lock for <paramref name="canonicalTargetPath"/>
    /// under <paramref name="writeLocksDir"/> (created if missing), polling with bounded
    /// exponential backoff up to <paramref name="backoffCap"/>. Returns <c>null</c> on
    /// timeout — never throws or blocks indefinitely (contract §3/§4).
    /// </summary>
    public static async Task<CrossProcessFileLock?> TryAcquireAsync(
        string writeLocksDir,
        string canonicalTargetPath,
        TimeSpan backoffCap,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(writeLocksDir);
        var lockPath = Path.Combine(writeLocksDir, ComputeLockFileName(canonicalTargetPath));

        var deadline = DateTime.UtcNow + backoffCap;
        var delay = InitialBackoffDelay;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return new CrossProcessFileLock(stream);
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                var remaining = deadline - DateTime.UtcNow;
                var wait = remaining < delay ? remaining : delay;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }

                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxBackoffDelay.TotalMilliseconds));
            }
        }
    }

    /// <summary>
    /// The lock-file name for a given canonical target path: the SHA-256 hex digest of
    /// the path (data-model.md "Write-Coordination Lock") — avoids filesystem-unsafe
    /// characters and keeps filenames constant-length.
    /// </summary>
    public static string ComputeLockFileName(string canonicalTargetPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalTargetPath));
        return Convert.ToHexStringLower(hash) + ".lock";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stream.Dispose();
    }
}
