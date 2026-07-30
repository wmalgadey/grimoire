namespace Grimoire.Domain.Guardrails;

/// <summary>
/// The result of a single policy evaluation for one tool call.
/// </summary>
/// <param name="Mode">
/// ADR-015/ADR-016: the matched write rule's <see cref="WriteMode"/> when this decision
/// allowed a write-scope call — the harness (<c>GuardedToolExecutor</c>) applies the
/// corresponding structural check (create-only existence check / frontmatter-only
/// body-preservation check). Always <see cref="WriteMode.ReadWrite"/> for read-scope
/// decisions and for denied decisions (meaningless there; never inspected).
/// </param>
public sealed record PolicyDecision(
    bool IsAllowed,
    string? DenialReason,
    WriteMode Mode = WriteMode.ReadWrite)
{
    /// <summary>
    /// Pre-ADR-016 computed convenience: <c>true</c> iff <see cref="Mode"/> is
    /// <see cref="WriteMode.CreateOnly"/>. Retained so every call site and test written
    /// against the boolean shape (before ADR-016 introduced the three-way <see cref="Mode"/>)
    /// keeps compiling and passing unchanged.
    /// </summary>
    public bool IsCreateOnly => Mode == WriteMode.CreateOnly;

    /// <summary>Constructs an allowed decision.</summary>
    /// <param name="mode">See <see cref="Mode"/>.</param>
    public static PolicyDecision Allow(WriteMode mode = WriteMode.ReadWrite) => new(true, null, mode);

    /// <summary>
    /// Pre-ADR-016 boolean overload, retained for source compatibility with every
    /// existing call site (e.g. <c>PolicyDecision.Allow(isCreateOnly: rule.CreateOnly)</c>).
    /// </summary>
    /// <param name="isCreateOnly">See <see cref="IsCreateOnly"/>.</param>
    public static PolicyDecision Allow(bool isCreateOnly) =>
        new(true, null, isCreateOnly ? WriteMode.CreateOnly : WriteMode.ReadWrite);

    /// <summary>Constructs a denied decision with an explicit reason.</summary>
    /// <param name="reason">
    /// One of: <c>no_rule</c>, <c>out_of_scope</c>, <c>traversal</c> (pure
    /// <see cref="SafetyPolicy"/> scope decisions), or one of the write-coordination
    /// reasons produced by <c>Coordination.SharedFileWriteGuard</c>.
    /// </param>
    public static PolicyDecision Deny(string reason) => new(false, reason);
}
