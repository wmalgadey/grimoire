using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T060 (014-wiki-storage-restructure, /speckit-analyze finding G2): extends
/// <see cref="QueryWriteConflictObservabilityTests"/>'s coverage of
/// <c>wiki.write_conflict.rejections_total</c> (ADR-015's original two reasons) to ADR-017's
/// remaining denying check, index.md's catalog-entry format. Writing this test surfaced a
/// real production gap in <c>GuardedToolExecutor</c>: <c>catalog_entry_malformed</c> was not
/// forwarded to <see cref="IToolCallInstrumentation.RecordWriteConflictRejected"/> — fixed
/// alongside this test.
///
/// 028-lint-at-scale (US3, Clarifications 2026-08-27, FSI-3, research.md R15): log.md's own
/// three ADR-017 reasons (<c>log_entry_not_prepended</c>/<c>log_entry_malformed_heading</c>/
/// <c>log_entry_missing_paragraph</c>) no longer deny at all — they moved to
/// <see cref="QueryWriteConflictRejectionAdr017MetricsTests.GuardedWrite_WithFormatDeviatingLogEntry_Commits_AndIncrementsFormatDeviationTotal_InsteadOfDenying"/>,
/// which proves the write now commits and the deviation-signal metric fires in place of the
/// old denial (index.md's catalog check is unaffected by this reclassification).
/// </summary>
[Collection("HubActivityListenerObservability")]
public class QueryWriteConflictRejectionAdr017MetricsTests
{
    [Fact]
    public async Task GuardedWrite_DeniedByCatalogEntryFormatCheck_Increments_WriteConflictRejectionsTotal_WithReasonLabel()
    {
        var measurements = new List<(long Value, string Reason)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "wiki.write_conflict.rejections_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var reason = tags.ToArray().FirstOrDefault(t => t.Key == "reason").Value?.ToString() ?? "";
            lock (measurements) { measurements.Add((value, reason)); }
        });
        listener.Start();

        var root = Path.Combine(Path.GetTempPath(), $"rejection-metric-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);
        try
        {
            var indexPath = Path.Combine(wikiRoot, "index.md");
            const string initialContent =
                "# Wiki Index\n\n## Concepts\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — Beschreibt Muster gegen Kaskadenausfälle — 3 Quellen\n";
            const string proposedContent =
                "# Wiki Index\n\n## Concepts\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — Beschreibt Muster gegen Kaskadenausfälle — 3 Quellen\n- [Retry Backoff](concepts/retry-backoff.md) — Missing status marker\n";
            await File.WriteAllTextAsync(indexPath, initialContent);

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writePrefixes: [wikiRoot + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var logger = new CaptureLogger<QueryWriteConflictRejectionAdr017MetricsTests>();
            var executor = new GuardedToolExecutor(
                policy, journal, wikiRoot,
                taskId: "t-rejection-metric",
                instrumentation: new QueryToolCallInstrumentation(logger),
                writeLocksDir: Path.Combine(root, "write-locks"),
                indexPath: indexPath);

            // Establish the CAS baseline the same way a real agent turn would, via the
            // guarded read_file tool, before attempting the denied write.
            await executor.ExecuteAsync(
                ToolRegistry.ReadFile, """{"path": "index.md"}""", turn: 1, CancellationToken.None);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                System.Text.Json.JsonSerializer.Serialize(new { path = "index.md", content = proposedContent }),
                turn: 2,
                CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Contains("catalog_entry_malformed", writeResult.Content, StringComparison.Ordinal);

            lock (measurements)
            {
                Assert.Contains(measurements, m => m.Value == 1L && m.Reason == "catalog_entry_malformed");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "log_entry_not_prepended",
        "## [2026-07-01] query | completed\n\nEarlier entry. Ref: turn-000.\n",
        "## [2026-07-01] query | rewritten\n\nRewritten entry. Ref: turn-000.\n")]
    [InlineData("log_entry_malformed_heading", "", "Just a note, no heading at all.\n")]
    [InlineData("log_entry_missing_paragraph", "", "## [2026-07-30] query | completed\n\n")]
    public async Task GuardedWrite_WithFormatDeviatingLogEntry_Commits_AndIncrementsFormatDeviationTotal_InsteadOfDenying(
        string expectedReason, string initialContent, string proposedContent)
    {
        var measurements = new List<(long Value, string Agent, string Mode, string Reason)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "wiki.log.format_deviation_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var tagArray = tags.ToArray();
            var agent = tagArray.FirstOrDefault(t => t.Key == "agent").Value?.ToString() ?? "";
            var mode = tagArray.FirstOrDefault(t => t.Key == "mode").Value?.ToString() ?? "";
            var reason = tagArray.FirstOrDefault(t => t.Key == "reason").Value?.ToString() ?? "";
            lock (measurements) { measurements.Add((value, agent, mode, reason)); }
        });
        listener.Start();

        var root = Path.Combine(Path.GetTempPath(), $"deviation-metric-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);
        try
        {
            var logPath = Path.Combine(wikiRoot, "log.md");
            await File.WriteAllTextAsync(logPath, initialContent);

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writePrefixes: [wikiRoot + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var logger = new CaptureLogger<QueryWriteConflictRejectionAdr017MetricsTests>();
            var executor = new GuardedToolExecutor(
                policy, journal, wikiRoot,
                taskId: "t-deviation-metric",
                instrumentation: new QueryToolCallInstrumentation(logger),
                writeLocksDir: Path.Combine(root, "write-locks"),
                logPath: logPath);

            await executor.ExecuteAsync(
                ToolRegistry.ReadFile, """{"path": "log.md"}""", turn: 1, CancellationToken.None);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                System.Text.Json.JsonSerializer.Serialize(new { path = "log.md", content = proposedContent }),
                turn: 2,
                CancellationToken.None);

            Assert.False(writeResult.IsError);
            Assert.Equal(proposedContent, await File.ReadAllTextAsync(logPath));

            lock (measurements)
            {
                Assert.Contains(measurements, m =>
                    m.Value == 1L && m.Agent == "query" && m.Mode == "replace" && m.Reason == expectedReason);
            }

            // T021's log-event half: name, level, and mandatory fields.
            var logEntry = Assert.Single(logger.Entries.Where(e => e.EventName == "wiki.log.format_deviation"));
            Assert.Equal(LogLevel.Warning, logEntry.Level);
            Assert.Equal("query", logEntry.Fields["agent"]);
            Assert.Equal("replace", logEntry.Fields["mode"]);
            Assert.Equal(logPath, logEntry.Fields["path"]);
            Assert.Equal(expectedReason, logEntry.Fields["reason"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 028-lint-at-scale (US3, T021, SC-009): "Never emitted for a conforming write" —
    /// the counterpart of the Theory above, proving the signal is deviation-triggered, not
    /// emitted on every log.md write.
    /// </summary>
    [Fact]
    public async Task GuardedWrite_WithConformingLogEntry_EmitsNoFormatDeviationSignal()
    {
        var measurements = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Grimoire.QueryAgent" && instrument.Name == "wiki.log.format_deviation_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => { lock (measurements) { measurements.Add(value); } });
        listener.Start();

        var root = Path.Combine(Path.GetTempPath(), $"deviation-metric-conforming-{Guid.NewGuid():N}");
        var wikiRoot = Path.Combine(root, "wiki");
        Directory.CreateDirectory(wikiRoot);
        try
        {
            var logPath = Path.Combine(wikiRoot, "log.md");
            await File.WriteAllTextAsync(logPath, string.Empty);

            var policy = new SafetyPolicy(
                wikiRoot,
                readPrefixes: [wikiRoot + Path.DirectorySeparatorChar],
                writePrefixes: [wikiRoot + Path.DirectorySeparatorChar]);
            var journal = new WriteJournal();
            var logger = new CaptureLogger<QueryWriteConflictRejectionAdr017MetricsTests>();
            var executor = new GuardedToolExecutor(
                policy, journal, wikiRoot,
                taskId: "t-deviation-metric-conforming",
                instrumentation: new QueryToolCallInstrumentation(logger),
                writeLocksDir: Path.Combine(root, "write-locks"),
                logPath: logPath);

            await executor.ExecuteAsync(ToolRegistry.ReadFile, """{"path": "log.md"}""", turn: 1, CancellationToken.None);

            const string conformingEntry = "## [2026-07-30] query | completed\n\nA well-formed entry.\n";
            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                System.Text.Json.JsonSerializer.Serialize(new { path = "log.md", content = conformingEntry }),
                turn: 2,
                CancellationToken.None);

            Assert.False(writeResult.IsError);

            lock (measurements)
            {
                Assert.Empty(measurements);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
