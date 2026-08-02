namespace Grimoire.Hub.RemediationTasks;

/// <summary>
/// Projects a parsed Remediation Task Record into the two context shapes both spawn
/// paths need (015-lint-board-parity US5, ADR-018/R6 "record-as-context" — the record is
/// the single source, so what a human sees and what the agent receives can never
/// diverge): the concatenated attached-context text handed to an execution run as its
/// ADR-007 user-prompt override (FR-011), and the prior human⇄agent messages handed to
/// the next message turn (FR-012). Both <see cref="RemediationRunCoordinator"/> and
/// <see cref="RemediationMessageTurnCoordinator"/> call this rather than duplicating the
/// entry-filtering logic.
/// </summary>
public static class RemediationTaskRecordContext
{
    /// <summary>
    /// Every <see cref="RemediationTaskRecordEntry.Context"/> entry, in attach order,
    /// joined into one block — null when there is none, so callers can omit the field/CLI
    /// argument entirely rather than passing an empty string (mirrors the existing
    /// <c>AttachedContext</c> null-omission convention in
    /// <c>AgentProcessHost.StartRemediationProcess</c>).
    /// </summary>
    public static string? BuildAttachedContext(IReadOnlyList<RemediationTaskRecordEntry> entries)
    {
        var texts = entries
            .OfType<RemediationTaskRecordEntry.Context>()
            .Select(c => c.Text)
            .ToList();

        return texts.Count == 0 ? null : string.Join("\n\n---\n\n", texts);
    }

    /// <summary>
    /// Every <see cref="RemediationTaskRecordEntry.Message"/> entry already in the record,
    /// in append order — the "prior" half of R6's record-as-context rule. Callers append
    /// the new human message to the record only <em>after</em> reading this, so it never
    /// includes the message the current turn is about.
    /// </summary>
    public static IReadOnlyList<RemediationPriorMessage> BuildPriorMessages(IReadOnlyList<RemediationTaskRecordEntry> entries)
        => entries
            .OfType<RemediationTaskRecordEntry.Message>()
            .Select(m => new RemediationPriorMessage(m.Sender, m.Text))
            .ToList();
}
