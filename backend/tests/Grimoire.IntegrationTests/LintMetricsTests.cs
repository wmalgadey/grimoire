using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.RunEvents;
using Grimoire.Hub;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T028 (013-lint-agent, US1) — business metric emission via in-process
/// <see cref="MeterListener"/> (mirrors QueryLifecycleMetricsTests.cs's pattern), for the
/// <c>wiki.lint.runs_total{outcome}</c> row of plan.md ## Observability > Business
/// Metrics this phase owns, plus the <c>wiki.lint.triggers_rejected_total</c> gap-fill
/// emitted at the same call site (T027's deviation note), and the agent-side
/// <c>lint.tool_calls_total</c> (the guarded tool executor runs inside
/// <c>Grimoire.LintAgent</c>, not the Hub).
/// </summary>
public class LintMetricsTests
{
    private static IReadOnlyList<T> Snapshot<T>(List<T> measurements)
    {
        lock (measurements)
        {
            return measurements.ToArray();
        }
    }

    private static void AddSynchronized<T>(List<T> measurements, T measurement)
    {
        lock (measurements)
        {
            measurements.Add(measurement);
        }
    }

    [Fact]
    public void HubMetrics_RecordLintRun_Increments_RunsTotal_WithOutcomeTag_ForEveryTerminalStatus()
    {
        var measurements = new List<(long Value, string Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.lint.runs_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(t => t.Key == "outcome").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, outcome));
        });
        listener.Start();

        HubMetrics.RecordLintRun("completed");
        HubMetrics.RecordLintRun("failed");

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == "completed");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Outcome == "failed");
    }

    [Fact]
    public void HubMetrics_RecordLintTriggerRejected_Increments_TriggersRejectedTotal()
    {
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.lint.triggers_rejected_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        HubMetrics.RecordLintTriggerRejected();

        Assert.Contains(Snapshot(measurements), v => v == 1L);
    }

    [Fact]
    public void LintAgentMetrics_RecordToolCall_Increments_ToolCallsTotal_WithToolAndDecisionTags()
    {
        var measurements = new List<(long Value, string Tool, string Decision)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.LintAgent" && instrument.Name == "lint.tool_calls_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var tagArray = tags.ToArray();
            var tool = tagArray.FirstOrDefault(t => t.Key == "tool").Value?.ToString() ?? "";
            var decision = tagArray.FirstOrDefault(t => t.Key == "decision").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, tool, decision));
        });
        listener.Start();

        LintAgentMetrics.RecordToolCall("write_file", "allowed");
        LintAgentMetrics.RecordToolCall("write_file", "denied");

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 1L && m.Tool == "write_file" && m.Decision == "allowed");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Tool == "write_file" && m.Decision == "denied");
    }

    // ── 028-lint-at-scale (US2, T011, plan.md ## Observability): wiki.lint.coverage_ratio /
    // wiki.lint.coverage_runs_total (renamed from the originally planned wiki.lint.runs_total
    // to avoid colliding with the pre-existing Hub-side metric of that name) ──────────────

    [Fact]
    public void LintAgentMetrics_RecordCoverage_RecordsRatio_AndIncrementsCoverageRunsTotal_WithCoverageStatusTag()
    {
        var ratioMeasurements = new List<(double Value, string Agent)>();
        var runsMeasurements = new List<(long Value, string Agent, string CoverageStatus)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name != "Grimoire.LintAgent")
            {
                return;
            }

            if (instrument.Name is "wiki.lint.coverage_ratio" or "wiki.lint.coverage_runs_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var agent = tags.ToArray().FirstOrDefault(t => t.Key == "agent").Value?.ToString() ?? "";
            AddSynchronized(ratioMeasurements, (value, agent));
        });
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var tagArray = tags.ToArray();
            var agent = tagArray.FirstOrDefault(t => t.Key == "agent").Value?.ToString() ?? "";
            var coverageStatus = tagArray.FirstOrDefault(t => t.Key == "coverage_status").Value?.ToString() ?? "";
            AddSynchronized(runsMeasurements, (value, agent, coverageStatus));
        });
        listener.Start();

        LintAgentMetrics.RecordCoverage(WikiCoverage.Compute(pagesTotal: 633, pagesConsidered: 611));

        var ratioSnapshot = Snapshot(ratioMeasurements);
        Assert.Contains(ratioSnapshot, m => m.Agent == "lint" && Math.Abs(m.Value - (611.0 / 633.0)) < 0.0001);

        var runsSnapshot = Snapshot(runsMeasurements);
        Assert.Contains(runsSnapshot, m => m.Value == 1L && m.Agent == "lint" && m.CoverageStatus == "partial");
    }

    // ── T038 (013-lint-agent, US2): wiki.lint.findings_total{category} and
    // wiki.lint.inbound_links_refreshed_total (plan.md ## Observability, T037) ──────────

    [Fact]
    public void HubMetrics_RecordLintFindings_Increments_FindingsTotal_WithCategoryTag()
    {
        var measurements = new List<(long Value, string Category)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.lint.findings_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var category = tags.ToArray().FirstOrDefault(t => t.Key == "category").Value?.ToString() ?? "";
            AddSynchronized(measurements, (value, category));
        });
        listener.Start();

        HubMetrics.RecordLintFindings("content_quality", 2);
        HubMetrics.RecordLintFindings("metadata_hygiene", 1);
        HubMetrics.RecordLintFindings("structure", 0);

        var snapshot = Snapshot(measurements);
        Assert.Contains(snapshot, m => m.Value == 2L && m.Category == "content_quality");
        Assert.Contains(snapshot, m => m.Value == 1L && m.Category == "metadata_hygiene");
        Assert.Contains(snapshot, m => m.Value == 0L && m.Category == "structure");
    }

    [Fact]
    public void HubMetrics_RecordLintInboundLinksRefreshed_Increments_InboundLinksRefreshedTotal()
    {
        var measurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.Hub" && instrument.Name == "wiki.lint.inbound_links_refreshed_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => AddSynchronized(measurements, value));
        listener.Start();

        HubMetrics.RecordLintInboundLinksRefreshed(3);

        Assert.Contains(Snapshot(measurements), v => v == 3L);
    }

    [Fact]
    public async Task LintRunCoordinator_CompletedRun_EmitsFindingsTotalPerCategory_AndInboundLinksRefreshedTotal()
    {
        // End-to-end through the coordinator (not HubMetrics in isolation): the terminal
        // event's narrative and touched-path count drive both new metrics automatically,
        // mirroring HubMetrics_RecordLintRun_...'s per-terminal-event coverage above.
        var categoryMeasurements = new List<(long Value, string Category)>();
        var refreshedMeasurements = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name != "Grimoire.Hub")
            {
                return;
            }

            if (instrument.Name == "wiki.lint.findings_total" || instrument.Name == "wiki.lint.inbound_links_refreshed_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "wiki.lint.findings_total")
            {
                var category = tags.ToArray().FirstOrDefault(t => t.Key == "category").Value?.ToString() ?? "";
                AddSynchronized(categoryMeasurements, (value, category));
            }
            else
            {
                lock (refreshedMeasurements)
                {
                    refreshedMeasurements.Add(value);
                }
            }
        });
        listener.Start();

        const string narrative =
            """
            ## Content Quality

            ### A contradiction

            Body.

            ### Another finding

            Body.

            ## Metadata Hygiene

            No metadata-hygiene findings.

            ## Structure

            No structure findings.
            """;

        using var harness = LintCoordinatorHarness.Create();
        harness.Launcher.ScriptedLintTerminalMetadata = new Dictionary<string, object?>
        {
            ["summary"] = narrative,
            ["createdPages"] = new[] { "tech/refreshed-1.md", "tech/refreshed-2.md" },
        };

        var result = await harness.Coordinator.TriggerAsync();
        var accepted = Assert.IsType<Grimoire.Hub.LintDispatch.LintSubmissionResult.Accepted>(result);
        await harness.WaitForTerminalAsync(accepted.Run.RunId);

        var categorySnapshot = Snapshot(categoryMeasurements);
        Assert.Contains(categorySnapshot, m => m.Value == 2L && m.Category == "content_quality");
        Assert.Contains(categorySnapshot, m => m.Value == 0L && m.Category == "metadata_hygiene");
        Assert.Contains(categorySnapshot, m => m.Value == 0L && m.Category == "structure");

        Assert.Contains(Snapshot(refreshedMeasurements), v => v == 2L);
    }
}
