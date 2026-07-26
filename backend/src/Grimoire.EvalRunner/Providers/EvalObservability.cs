using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Grimoire.EvalRunner.Providers;

/// <summary>
/// Emits the 007 eval-harness observability contract, moved unchanged into the eval
/// runner by 009: the `grimoire.eval.gate_resolutions_total` counter, the
/// `eval_provider_resolved` and `eval_sample_timeout` structured log events, and the
/// `eval.gate_resolution` trace span. The ActivitySource/Meter name stays
/// "Grimoire.AgentEvals" — it is part of the published observability contract
/// (dashboards, 007 integration tests) and renaming it would break correlation.
/// </summary>
public static class EvalObservability
{
    public static readonly ActivitySource ActivitySource = new("Grimoire.AgentEvals", "1.0.0");

    private static readonly Meter Meter = new("Grimoire.AgentEvals", "1.0.0");

    private static readonly Counter<long> GateResolutionsTotal = Meter.CreateCounter<long>(
        "grimoire.eval.gate_resolutions_total",
        description: "Number of eval-suite provider gate resolutions, labeled by provider and outcome.");

    private static readonly EventId ProviderResolvedEvent = new(1, "eval_provider_resolved");
    private static readonly EventId SampleTimeoutEvent = new(2, "eval_sample_timeout");

    public static void RecordGateResolution(ILogger logger, EvalGateOutcome outcome)
    {
        var provider = ProviderLabel(outcome.Configuration.Kind);
        var outcomeLabel = OutcomeLabel(outcome.Status);
        var model = outcome.Configuration.Model;

        using var span = ActivitySource.StartActivity("eval.gate_resolution");
        span?.SetTag("provider", provider);
        span?.SetTag("outcome", outcomeLabel);
        span?.SetTag("model", model);

        GateResolutionsTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcomeLabel));

        logger.LogInformation(
            ProviderResolvedEvent,
            "Eval provider gate resolved. provider={provider} outcome={outcome} model={model} reason={reason}",
            provider,
            outcomeLabel,
            model,
            outcome.Reason);
    }

    public static void RecordSampleTimeout(ILogger logger, string evalName, string provider, string? model, double timeoutSeconds)
    {
        logger.LogWarning(
            SampleTimeoutEvent,
            "Eval sample exceeded the provider call timeout. eval_name={eval_name} provider={provider} model={model} timeout_seconds={timeout_seconds}",
            evalName,
            provider,
            model,
            timeoutSeconds);
    }

    public static string ProviderLabel(ProviderKind kind) => kind switch
    {
        ProviderKind.Anthropic => "anthropic",
        ProviderKind.Affordable => "affordable",
        _ => "none",
    };

    private static string OutcomeLabel(EvalGateStatus status) => status switch
    {
        EvalGateStatus.Enabled => "enabled",
        EvalGateStatus.Skipped => "skipped",
        EvalGateStatus.ConfigurationError => "configuration_error",
        _ => "unknown",
    };
}
