using System.Diagnostics.Metrics;
using System.Text.Json;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.LintAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T054 (026-guarded-tool-surface, US3, Principle IV): validates that
/// <c>wiki.read.invocations_total</c>'s <c>shape</c> label is correct for full, range and
/// frontmatter reads, read from the production composition root — the real
/// <see cref="LintAgentMetrics"/> static meter via <see cref="LintToolCallInstrumentation"/>,
/// exercised through <see cref="GuardedToolExecutor"/> directly. Mirrors
/// <c>IngestObservabilityMetricsTests</c>' <see cref="MeterListener"/> idiom, since this is a
/// label-correctness check rather than a span-parenting one (no <c>AgentLoop</c> needed).
/// </summary>
public class LintReadShapeObservabilityTests
{
    private static readonly ToolRegistry RangedReadRegistry = new(
    [
        ToolRegistry.ListFilesDefinition,
        ToolRegistry.RangedReadFileDefinition,
        ToolRegistry.WriteFileDefinition,
    ]);

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
    public async Task ReadInvocations_AreLabelledByShape_ForFullRangeAndFrontmatterReads()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lint-read-shape-observability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "page.md"), "---\ntitle: Page\n---\nline one\nline two\n");

            var policy = new SafetyPolicy(root, readPrefixes: [root + Path.DirectorySeparatorChar], writePrefixes: []);
            var executor = new GuardedToolExecutor(
                policy, new WriteJournal(), root, taskId: "run-read-shape-obs",
                registry: RangedReadRegistry,
                instrumentation: new LintToolCallInstrumentation(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

            var measurements = new List<(long Value, string Shape)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Grimoire.LintAgent" &&
                    instrument.Name == "wiki.read.invocations_total")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                var shape = tags.ToArray().FirstOrDefault(t => t.Key == "shape").Value?.ToString() ?? "";
                AddSynchronized(measurements, (value, shape));
            });
            listener.Start();

            await executor.ExecuteAsync(
                ToolRegistry.ReadFile, JsonSerializer.Serialize(new { path = "page.md" }),
                turn: 1, CancellationToken.None);
            await executor.ExecuteAsync(
                ToolRegistry.ReadFile, JsonSerializer.Serialize(new { path = "page.md", offset = 3, limit = 1 }),
                turn: 2, CancellationToken.None);
            await executor.ExecuteAsync(
                ToolRegistry.ReadFile, JsonSerializer.Serialize(new { path = "page.md", frontmatter_only = true }),
                turn: 3, CancellationToken.None);

            var snapshot = Snapshot(measurements);
            Assert.Contains(snapshot, m => m.Shape == "full");
            Assert.Contains(snapshot, m => m.Shape == "range");
            Assert.Contains(snapshot, m => m.Shape == "frontmatter");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
