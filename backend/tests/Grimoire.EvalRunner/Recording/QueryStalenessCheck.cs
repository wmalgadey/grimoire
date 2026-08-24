using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;

namespace Grimoire.EvalRunner.Recording;

/// <summary>
/// Staleness evaluation for Query scenarios (T099, 008-query-agent) — mirrors
/// <see cref="StalenessCheck"/> for <see cref="QueryScenarioDefinition"/>, whose
/// fingerprint sources differ (Query's own instruction surface, no default-user-prompt
/// document, never judge-scored).
/// </summary>
public static class QueryStalenessCheck
{
    public static string RefreshCommand(string scenarioId)
        => $"dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario {scenarioId}";

    public static IReadOnlyDictionary<string, string> CurrentFingerprints(QueryScenarioDefinition scenario, EvalPaths paths)
        => Fingerprints.Compute(
            paths.QuerySystemPromptPath,
            defaultUserPromptPath: null,
            paths.QueryPolicyPath,
            paths.FixtureWikiRoot(scenario.FixtureName),
            scenario.StableSerialization(),
            judgePromptTemplate: null);

    public static ScenarioTrustReport Evaluate(QueryScenarioDefinition scenario, RecordingStore store, EvalPaths paths)
    {
        if (!store.HasScenario(scenario.Id))
        {
            return new ScenarioTrustReport(
                scenario.Id,
                TrustStatus.Missing,
                [],
                $"No recording exists for scenario '{scenario.Id}'. Capture one with: {RefreshCommand(scenario.Id)}",
                Manifest: null);
        }

        RecordingManifest manifest;
        try
        {
            manifest = store.LoadManifest(scenario.Id);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            return new ScenarioTrustReport(
                scenario.Id,
                TrustStatus.Mismatch,
                [],
                $"Manifest for scenario '{scenario.Id}' is unreadable ({ex.Message}). Re-capture with: {RefreshCommand(scenario.Id)}",
                Manifest: null);
        }

        var current = CurrentFingerprints(scenario, paths);
        var changed = new List<string>();
        foreach (var key in current.Keys.Union(manifest.Fingerprints.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var currentValue = current.TryGetValue(key, out var c) ? c : null;
            var recordedValue = manifest.Fingerprints.TryGetValue(key, out var r) ? r : null;
            if (!string.Equals(currentValue, recordedValue, StringComparison.Ordinal))
            {
                changed.Add(key);
            }
        }

        if (changed.Count > 0)
        {
            return new ScenarioTrustReport(
                scenario.Id,
                TrustStatus.Stale,
                changed,
                $"Recordings for '{scenario.Id}' are stale (changed: {string.Join(", ", changed)}). " +
                $"Refresh with: {RefreshCommand(scenario.Id)}",
                manifest);
        }

        return new ScenarioTrustReport(scenario.Id, TrustStatus.Trusted, [], Detail: null, manifest);
    }
}
