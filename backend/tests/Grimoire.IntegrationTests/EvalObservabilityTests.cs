using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grimoire.AgentEvals;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T028/T030/T032/T034 — deterministic validation of the eval-harness observability
/// contract (plan.md ## Observability: `eval_provider_resolved`, `eval_sample_timeout`,
/// `grimoire.eval.gate_resolutions_total`, `eval.gate_resolution`). Runs in the standard PR
/// pipeline (this project, unlike Grimoire.AgentEvals, requires no provider secret) because
/// <see cref="EvalObservability"/>'s emission functions are pure — they take an already
/// resolved <see cref="EvalGateOutcome"/> rather than reading the environment themselves.
/// </summary>
public class EvalObservabilityTests
{
    [Fact]
    public void RecordGateResolution_Enabled_Affordable_EmitsProviderResolvedEventWithExpectedFields()
    {
        var outcome = new EvalGateOutcome(
            EvalGateStatus.Enabled,
            new ProviderConfiguration(ProviderKind.Affordable, "http://localhost:4000", "nvidia-model", HasCredential: true),
            Reason: null);
        var logger = new CaptureLogger<EvalObservabilityTests>();

        EvalObservability.RecordGateResolution(logger, outcome);

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_provider_resolved");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("affordable", entry.Fields["provider"]?.ToString());
        Assert.Equal("enabled", entry.Fields["outcome"]?.ToString());
        Assert.Equal("nvidia-model", entry.Fields["model"]?.ToString());
        Assert.True(entry.Fields.ContainsKey("reason"));
    }

    [Fact]
    public void RecordGateResolution_Enabled_Anthropic_EmitsProviderResolvedEventWithExpectedFields()
    {
        var outcome = new EvalGateOutcome(
            EvalGateStatus.Enabled,
            new ProviderConfiguration(ProviderKind.Anthropic, BaseUrl: null, "claude-opus-4-8", HasCredential: true),
            Reason: null);
        var logger = new CaptureLogger<EvalObservabilityTests>();

        EvalObservability.RecordGateResolution(logger, outcome);

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_provider_resolved");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("anthropic", entry.Fields["provider"]?.ToString());
        Assert.Equal("enabled", entry.Fields["outcome"]?.ToString());
        Assert.Equal("claude-opus-4-8", entry.Fields["model"]?.ToString());
    }

    [Fact]
    public void RecordGateResolution_Skipped_EmitsProviderResolvedEventWithExpectedFields()
    {
        var outcome = new EvalGateOutcome(EvalGateStatus.Skipped, ProviderConfiguration.None, EvalProviderResolver.NeitherConfiguredReason);
        var logger = new CaptureLogger<EvalObservabilityTests>();

        EvalObservability.RecordGateResolution(logger, outcome);

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_provider_resolved");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("none", entry.Fields["provider"]?.ToString());
        Assert.Equal("skipped", entry.Fields["outcome"]?.ToString());
        Assert.Equal(EvalProviderResolver.NeitherConfiguredReason, entry.Fields["reason"]?.ToString());
    }

    [Fact]
    public void RecordGateResolution_ConfigurationError_EmitsProviderResolvedEventWithExpectedFields()
    {
        var outcome = new EvalGateOutcome(EvalGateStatus.ConfigurationError, ProviderConfiguration.None, EvalProviderResolver.BothConfiguredReason);
        var logger = new CaptureLogger<EvalObservabilityTests>();

        EvalObservability.RecordGateResolution(logger, outcome);

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_provider_resolved");
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("none", entry.Fields["provider"]?.ToString());
        Assert.Equal("configuration_error", entry.Fields["outcome"]?.ToString());
        Assert.Equal(EvalProviderResolver.BothConfiguredReason, entry.Fields["reason"]?.ToString());
    }

    [Fact]
    public void RecordSampleTimeout_EmitsExpectedNameLevelAndFields()
    {
        var logger = new CaptureLogger<EvalObservabilityTests>();

        EvalObservability.RecordSampleTimeout(logger, "sc006-1", "affordable", "nvidia-model", 120);

        var entry = Assert.Single(logger.Entries, e => e.EventName == "eval_sample_timeout");
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("sc006-1", entry.Fields["eval_name"]?.ToString());
        Assert.Equal("affordable", entry.Fields["provider"]?.ToString());
        Assert.Equal("nvidia-model", entry.Fields["model"]?.ToString());
        Assert.Equal("120", entry.Fields["timeout_seconds"]?.ToString());
    }

    [Fact]
    public void RecordGateResolution_IncrementsGateResolutionsCounter_ForEachProviderOutcomeCombination()
    {
        var measurements = new ConcurrentBag<(string Provider, string Outcome, long Value)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.AgentEvals" && instrument.Name == "grimoire.eval.gate_resolutions_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            string? provider = null;
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "provider")
                {
                    provider = tag.Value?.ToString();
                }
                else if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                }
            }

            measurements.Add((provider ?? string.Empty, outcome ?? string.Empty, measurement));
        });
        listener.Start();

        var logger = new CaptureLogger<EvalObservabilityTests>();
        EvalObservability.RecordGateResolution(
            logger,
            new EvalGateOutcome(EvalGateStatus.Enabled, new ProviderConfiguration(ProviderKind.Affordable, "http://localhost:4000", "nvidia-model", true), null));
        EvalObservability.RecordGateResolution(
            logger,
            new EvalGateOutcome(EvalGateStatus.Enabled, new ProviderConfiguration(ProviderKind.Anthropic, null, "claude-opus-4-8", true), null));
        EvalObservability.RecordGateResolution(
            logger,
            new EvalGateOutcome(EvalGateStatus.Skipped, ProviderConfiguration.None, "skip reason"));
        EvalObservability.RecordGateResolution(
            logger,
            new EvalGateOutcome(EvalGateStatus.ConfigurationError, ProviderConfiguration.None, "conflict reason"));

        Assert.Contains(measurements, m => m.Provider == "affordable" && m.Outcome == "enabled" && m.Value == 1);
        Assert.Contains(measurements, m => m.Provider == "anthropic" && m.Outcome == "enabled" && m.Value == 1);
        Assert.Contains(measurements, m => m.Provider == "none" && m.Outcome == "skipped" && m.Value == 1);
        Assert.Contains(measurements, m => m.Provider == "none" && m.Outcome == "configuration_error" && m.Value == 1);
    }

    [Fact]
    public void RecordGateResolution_EmitsGateResolutionSpan_WithExpectedAttributes()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "Grimoire.AgentEvals",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var logger = new CaptureLogger<EvalObservabilityTests>();
        EvalObservability.RecordGateResolution(
            logger,
            new EvalGateOutcome(EvalGateStatus.Enabled, new ProviderConfiguration(ProviderKind.Affordable, "http://localhost:4000", "nvidia-model-span-probe", true), null));

        var span = Assert.Single(activities, a => a.OperationName == "eval.gate_resolution" && GetTag(a, "model") == "nvidia-model-span-probe");
        Assert.Equal("affordable", GetTag(span, "provider"));
        Assert.Equal("enabled", GetTag(span, "outcome"));
        Assert.Equal("nvidia-model-span-probe", GetTag(span, "model"));
    }

    private static string? GetTag(Activity activity, string tagName)
        => activity.TagObjects.FirstOrDefault(tag => tag.Key == tagName).Value?.ToString();
}
