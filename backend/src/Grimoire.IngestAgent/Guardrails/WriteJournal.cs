namespace Grimoire.IngestAgent.Guardrails;

/// <summary>
/// Records the prior state of a file before every allowed write so the run can be
/// rolled back in reverse order on failure (FR-013, R7).
/// </summary>
internal sealed record WriteJournalEntry(
    string Path,
    bool ExistedBefore,
    byte[]? PreviousContent);

/// <summary>
/// In-memory per-run write journal. Records prior state before each guarded write
/// and can restore all entries in reverse order on failure.
/// The task artifact and ingest log are harness-owned and exempt from rollback.
/// </summary>
public sealed class WriteJournal
{
    private readonly List<WriteJournalEntry> _entries = [];

    /// <summary>
    /// Journals the current state of <paramref name="path"/> before it is overwritten.
    /// Must be called before every write, inside the guarded executor.
    /// </summary>
    public async Task RecordAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            var content = await File.ReadAllBytesAsync(path, cancellationToken);
            _entries.Add(new WriteJournalEntry(path, true, content));
        }
        else
        {
            _entries.Add(new WriteJournalEntry(path, false, null));
        }
    }

    /// <summary>
    /// Rolls back all journaled writes in reverse order.
    /// Returns a per-path outcome dictionary where <c>true</c> = restored successfully.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, bool>> RollbackAsync(CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, bool>(StringComparer.Ordinal);

        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            try
            {
                if (entry.ExistedBefore && entry.PreviousContent is not null)
                {
                    await File.WriteAllBytesAsync(entry.Path, entry.PreviousContent, cancellationToken);
                }
                else if (!entry.ExistedBefore && File.Exists(entry.Path))
                {
                    File.Delete(entry.Path);
                }
                results[entry.Path] = true;
            }
            catch
            {
                results[entry.Path] = false;
            }
        }

        return results;
    }

    /// <summary>All paths that have been journaled so far in this run.</summary>
    public IReadOnlyList<string> JournaledPaths => _entries.Select(e => e.Path).ToList();
}
