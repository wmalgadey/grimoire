using Grimoire.EvalRunner.Providers;

namespace Grimoire.AgentEvals;

/// <summary>
/// Gates a live agent-behavior eval on GRIMOIRE_EVAL=1 plus a resolvable model provider
/// (data-model.md#ProviderConfiguration) — skips with a diagnosable reason rather than
/// failing when the environment isn't configured for live sampling (FR-012: fails loudly
/// only on an ambiguous/conflicting configuration).
/// </summary>
public sealed class EvalFactAttribute : FactAttribute
{
    public EvalFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GRIMOIRE_EVAL"), "1", StringComparison.Ordinal))
        {
            Skip = "Set GRIMOIRE_EVAL=1 and either ANTHROPIC_AUTH_TOKEN or the GRIMOIRE_EVAL_PROVIDER_* " +
                "variables to run agent-behavior evals.";
            return;
        }

        var outcome = EvalProviderResolver.Resolve();
        switch (outcome.Status)
        {
            case EvalGateStatus.Enabled:
                break;
            case EvalGateStatus.Skipped:
                Skip = outcome.Reason;
                break;
            case EvalGateStatus.ConfigurationError:
                throw new InvalidOperationException(outcome.Reason);
            default:
                throw new InvalidOperationException($"Unhandled eval gate status: {outcome.Status}.");
        }
    }
}

public static class EvalGate
{
    public static int ResolveSampleCount()
    {
        var raw = Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_SAMPLES");
        if (!int.TryParse(raw, out var value))
        {
            return 10;
        }

        return Math.Clamp(value, 1, 20);
    }
}
