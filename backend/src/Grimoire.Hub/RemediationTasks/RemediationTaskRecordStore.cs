using System.Collections.Concurrent;
using Grimoire.Hub.Runtime.Paths;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// Owns the durable, append-only Remediation Task Record (015-lint-board-parity
/// data-model.md, ADR-014's Conversation Record shape one level down): created at task
/// materialization with frontmatter + the verbatim proposal entry, appended-to for
/// attached context, message exchanges, and the terminal outcome — earlier bytes are
/// never modified, and the record survives terminal outcomes (FR-014). Concrete class,
/// directly injected — persistence exemption (Constitution I / ADR-010); confined to
/// <c>Grimoire.Hub.RemediationTasks</c> (containment-tested by
/// RemediationTasksContainmentRuleTests). The per-task lock mirrors
/// <c>ConversationRecordStore</c>'s defense-in-depth serialization.
/// </summary>
public sealed class RemediationTaskRecordStore
{
    private readonly ResolvedGrimoirePaths _paths;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskLocks = new();

    public RemediationTaskRecordStore(ResolvedGrimoirePaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Creates the record at materialization: frontmatter + the proposal entry in a
    /// single write. Idempotent per task — a record that already exists is left
    /// untouched (append-only: recorded bytes are never rewritten), mirroring the
    /// repository's <c>INSERT ... ON CONFLICT DO NOTHING</c> materialization semantics.
    /// </summary>
    public async Task CreateAsync(
        string taskId, string runId, DateTimeOffset proposedAt,
        string title, string description, string? targetPath,
        CancellationToken cancellationToken = default)
    {
        var gate = _taskLocks.GetOrAdd(taskId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = _paths.RemediationTaskRecordPathFor(taskId);
            if (File.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(_paths.RemediationTasksDir);
            var content = RemediationTaskRecordFormat.BuildRecordHeader(taskId, runId, proposedAt)
                          + RemediationTaskRecordFormat.BuildProposalBlock(
                              new RemediationTaskRecordEntry.Proposal(title, description, targetPath));
            await File.WriteAllTextAsync(path, content, RemediationTaskRecordFormat.Encoding, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Appends one human-attached context entry (FR-011), verbatim.</summary>
    public Task AppendContextAsync(string taskId, string text, DateTimeOffset attachedAt, CancellationToken cancellationToken = default)
        => AppendBlockAsync(
            taskId,
            RemediationTaskRecordFormat.BuildContextBlock(new RemediationTaskRecordEntry.Context(attachedAt, text)),
            cancellationToken);

    /// <summary>Appends one side of a human⇄agent exchange (FR-012); <paramref name="sender"/> is <c>human</c> or <c>agent</c>.</summary>
    public Task AppendMessageAsync(string taskId, string sender, string text, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
        => AppendBlockAsync(
            taskId,
            RemediationTaskRecordFormat.BuildMessageBlock(new RemediationTaskRecordEntry.Message(sender, timestamp, text)),
            cancellationToken);

    /// <summary>
    /// Appends the terminal outcome entry. <paramref name="reason"/> is mandatory for the
    /// <c>failed</c>/<c>not_applicable</c> states (FR-005/FR-018/SC-007 — same invariant
    /// as the state machine's); <paramref name="summary"/> is the optional agent-authored
    /// summary, verbatim (empty when absent).
    /// </summary>
    public Task AppendOutcomeAsync(
        string taskId, string state, string? reason, DateTimeOffset completedAt, string summary = "",
        CancellationToken cancellationToken = default)
    {
        if (state is RemediationTaskStates.Failed or RemediationTaskStates.NotApplicable &&
            string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                $"reason is mandatory for the '{state}' outcome (FR-005/FR-018/SC-007).", nameof(reason));
        }

        return AppendBlockAsync(
            taskId,
            RemediationTaskRecordFormat.BuildOutcomeBlock(new RemediationTaskRecordEntry.Outcome(state, reason, completedAt, summary)),
            cancellationToken);
    }

    /// <summary>
    /// Reads and parses the full record — the source of prior-message context for
    /// message turns and of the task history view; readable at any time, including after
    /// terminal outcomes (FR-014). A missing record yields an
    /// <see cref="RemediationTaskRecordParseResult.Unreadable"/> result.
    /// </summary>
    public async Task<RemediationTaskRecordParseResult> ReadAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var path = _paths.RemediationTaskRecordPathFor(taskId);
        if (!File.Exists(path))
        {
            return new RemediationTaskRecordParseResult.Unreadable($"no record exists for task '{taskId}'");
        }

        var content = await File.ReadAllTextAsync(path, RemediationTaskRecordFormat.Encoding, cancellationToken);
        return RemediationTaskRecordFormat.Parse(content);
    }

    /// <summary>Single append-mode write of one complete block; the record must already exist (created at materialization).</summary>
    private async Task AppendBlockAsync(string taskId, string block, CancellationToken cancellationToken)
    {
        var gate = _taskLocks.GetOrAdd(taskId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = _paths.RemediationTaskRecordPathFor(taskId);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Remediation task record for '{taskId}' does not exist — records are created at materialization.");
            }

            await File.AppendAllTextAsync(path, block, RemediationTaskRecordFormat.Encoding, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
