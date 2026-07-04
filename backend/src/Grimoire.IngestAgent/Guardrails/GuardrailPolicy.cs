namespace Grimoire.IngestAgent.Guardrails;

public enum GuardrailAction
{
    Read,
    Write,
}

public sealed record GuardrailRule(string Id, GuardrailAction Action, string PathPrefix, bool Allow, string Reason);

public sealed record GuardrailPolicy(
    string Version,
    bool DenyByDefault,
    IReadOnlyList<string> WriteAllowPrefixes,
    IReadOnlyList<string> ReadAllowPaths,
    IReadOnlyList<GuardrailRule> Rules);

public sealed record GuardrailDecision(bool IsAllowed, string Reason, string? RuleId = null);
