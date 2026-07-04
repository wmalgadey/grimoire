namespace Grimoire.Domain.Guardrails;

/// <summary>
/// The result of a single policy evaluation for one tool call.
/// </summary>
public sealed record PolicyDecision(
    bool IsAllowed,
    string? DenialReason)
{
    /// <summary>Constructs an allowed decision.</summary>
    public static PolicyDecision Allow() => new(true, null);

    /// <summary>Constructs a denied decision with an explicit reason.</summary>
    /// <param name="reason">
    /// One of: <c>no_rule</c>, <c>out_of_scope</c>, <c>traversal</c>.
    /// </param>
    public static PolicyDecision Deny(string reason) => new(false, reason);
}
