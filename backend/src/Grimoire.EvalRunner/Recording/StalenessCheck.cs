using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Scoring;
using Grimoire.EvalRunner.Workspace;

namespace Grimoire.EvalRunner.Recording;

/// <summary>
/// Trust status of a recording/replay result (data-model.md#TrustStatus). Always derived,
/// never persisted as authority.
/// </summary>
public enum TrustStatus
{
    Trusted,
    Stale,
    Missing,
    Mismatch,
}

/// <summary>One scenario's staleness/provenance verdict.</summary>
public sealed record ScenarioTrustReport(
    string ScenarioId,
    TrustStatus Status,
    IReadOnlyList<string> ChangedFingerprints,
    string? Detail,
    RecordingManifest? Manifest);

/// <summary>
/// Staleness evaluation (spec 009 FR-008): recomputes the fingerprint set against the
/// current workspace inputs and diffs it against the scenario manifest. Any drift marks
/// every recording of the scenario stale; the report names the changed kinds and the
/// exact capture invocation that refreshes them.
/// </summary>
public static class StalenessCheck
{
    public static string RefreshCommand(string scenarioId)
        => $"dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario {scenarioId}";

    public static IReadOnlyDictionary<string, string> CurrentFingerprints(ScenarioDefinition scenario, EvalPaths paths)
        => Fingerprints.Compute(
            paths.SystemPromptPath,
            paths.DefaultUserPromptPath,
            paths.PolicyPath,
            paths.FixtureWikiRoot(scenario.FixtureName),
            scenario.StableSerialization(),
            scenario.JudgeScored ? JudgeScoring.JudgePromptTemplate : null);

    public static ScenarioTrustReport Evaluate(ScenarioDefinition scenario, RecordingStore store, EvalPaths paths)
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
