namespace Grimoire.Domain.Guardrails;

/// <summary>
/// The result of a single policy evaluation for one tool call.
/// </summary>
/// <param name="IsCreateOnly">
/// ADR-015: <c>true</c> when this decision allowed a write-scope call whose matched rule
/// is <c>create-only</c> — the harness (<c>GuardedToolExecutor</c>) must deny the write if
/// the canonical target already exists on disk. Always <c>false</c> for read-scope
/// decisions and for denied decisions.
/// </param>
public sealed record PolicyDecision(
    bool IsAllowed,
    string? DenialReason,
    bool IsCreateOnly = false)
{
    /// <summary>Constructs an allowed decision.</summary>
    /// <param name="isCreateOnly">See <see cref="IsCreateOnly"/>.</param>
    public static PolicyDecision Allow(bool isCreateOnly = false) => new(true, null, isCreateOnly);

    /// <summary>Constructs a denied decision with an explicit reason.</summary>
    /// <param name="reason">
    /// One of: <c>no_rule</c>, <c>out_of_scope</c>, <c>traversal</c>.
    /// </param>
    public static PolicyDecision Deny(string reason) => new(false, reason);
}
