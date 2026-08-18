using System.Diagnostics.Metrics;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.QueryAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T060 (014-wiki-storage-restructure, /speckit-analyze finding G2): extends
/// <see cref="QueryWriteConflictObservabilityTests"/>'s coverage of
/// <c>wiki.write_conflict.rejections_total</c> (ADR-015's original two reasons) to
/// ADR-017's four new denial reasons. Writing this test surfaced a real production gap in
/// <c>GuardedToolExecutor</c>: only three of the four reasons were actually forwarded to
/// <see cref="IToolCallInstrumentation.RecordWriteConflictRejected"/> —
/// <c>catalog_entry_malformed</c> was missing — fixed alongside this test.
/// </summary>
public class QueryWriteConflictRejectionAdr017MetricsTests
{
    [Theory]
    [InlineData("log_entry_not_prepended")]
    [InlineData("log_entry_malformed_heading")]
    [InlineData("log_entry_missing_paragraph")]
    [InlineData("catalog_entry_malformed")]
    public async Task GuardedWrite_DeniedByAdr017Check_Increments_WriteConflictRejectionsTotal_WithReasonLabel(string expectedReason)
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
            var logPath = Path.Combine(wikiRoot, "log.md");
            var indexPath = Path.Combine(wikiRoot, "index.md");
            var (targetPath, relativePath, initialContent, proposedContent) = expectedReason switch
            {
                "log_entry_not_prepended" => (
                    logPath, "log.md",
                    "## [2026-07-01] query | completed\n\nEarlier entry. Ref: turn-000.\n",
                    "## [2026-07-01] query | rewritten\n\nRewritten entry. Ref: turn-000.\n"),
                "log_entry_malformed_heading" => (
                    logPath, "log.md",
                    "",
                    "Just a note, no heading at all.\n"),
                "log_entry_missing_paragraph" => (
                    logPath, "log.md",
                    "",
                    "## [2026-07-30] query | completed\n\n"),
                "catalog_entry_malformed" => (
                    indexPath, "index.md",
                    "# Wiki Index\n\n## Concepts\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — Beschreibt Muster gegen Kaskadenausfälle — 3 Quellen\n",
                    "# Wiki Index\n\n## Concepts\n\n- [Circuit Breaker](concepts/circuit-breaker.md) — Beschreibt Muster gegen Kaskadenausfälle — 3 Quellen\n- [Retry Backoff](concepts/retry-backoff.md) — Missing status marker\n"),
                _ => throw new InvalidOperationException($"Unhandled reason: {expectedReason}")
            };
            await File.WriteAllTextAsync(targetPath, initialContent);

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
                logPath: logPath,
                indexPath: indexPath);

            // Establish the CAS baseline the same way a real agent turn would, via the
            // guarded read_file tool, before attempting the denied write.
            await executor.ExecuteAsync(
                ToolRegistry.ReadFile, $$"""{"path": "{{relativePath}}"}""", turn: 1, CancellationToken.None);

            var writeResult = await executor.ExecuteAsync(
                ToolRegistry.WriteFile,
                System.Text.Json.JsonSerializer.Serialize(new { path = relativePath, content = proposedContent }),
                turn: 2,
                CancellationToken.None);

            Assert.True(writeResult.IsError);
            Assert.Contains(expectedReason, writeResult.Content, StringComparison.Ordinal);

            lock (measurements)
            {
                Assert.Contains(measurements, m => m.Value == 1L && m.Reason == expectedReason);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
