namespace Grimoire.IngestAgent.Guardrails;

public sealed class GuardrailEvaluator
{
    private readonly GuardrailPolicy _policy;

    public GuardrailEvaluator(GuardrailPolicy policy)
    {
        _policy = policy;
    }

    public GuardrailDecision Evaluate(GuardrailAction action, string repoRelativePath)
    {
        var normalized = repoRelativePath.Replace('\\', '/').TrimStart('/');

        foreach (var rule in _policy.Rules)
        {
            if (rule.Action != action)
            {
                continue;
            }

            if (normalized.StartsWith(rule.PathPrefix, StringComparison.Ordinal))
            {
                return rule.Allow
                    ? new GuardrailDecision(true, rule.Reason, rule.Id)
                    : new GuardrailDecision(false, rule.Reason, rule.Id);
            }
        }

        if (action == GuardrailAction.Write)
        {
            if (_policy.WriteAllowPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return new GuardrailDecision(true, "Allowed by write_allow_prefixes.");
            }
        }
        else if (_policy.ReadAllowPaths.Any(path => normalized.StartsWith(path, StringComparison.Ordinal)))
        {
            return new GuardrailDecision(true, "Allowed by read_allow_paths.");
        }

        return _policy.DenyByDefault
            ? new GuardrailDecision(false, "Denied by default policy.")
            : new GuardrailDecision(true, "Allowed because deny_by_default is disabled.");
    }
}
