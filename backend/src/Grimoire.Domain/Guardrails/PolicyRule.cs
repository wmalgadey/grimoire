namespace Grimoire.Domain.Guardrails;

/// <summary>
/// A single allow rule in the safety policy. Path prefixes are declared
/// relative to the repository root; the harness resolves them to canonical
/// absolute paths at load time before policy evaluation.
/// </summary>
public sealed record PolicyRule(string PathPrefix);
