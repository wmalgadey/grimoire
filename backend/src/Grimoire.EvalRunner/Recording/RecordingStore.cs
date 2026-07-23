using System.Security.Cryptography;
using System.Text.Json;
using Grimoire.IngestAgent.AgentCore.Adapters.Replay;

namespace Grimoire.EvalRunner.Recording;

/// <summary>One sample entry in a scenario manifest.</summary>
public sealed record ManifestSample(string File, string Sha256, string TaskId);

/// <summary>
/// `manifest.json` of one scenario's recording set: provenance (model, capture time) and
/// the staleness authority (fingerprints), per contracts/recording-format.md.
/// </summary>
public sealed record RecordingManifest(
    int SchemaVersion,
    string ScenarioId,
    DateTimeOffset CapturedAt,
    string Model,
    string ProviderKind,
    IReadOnlyDictionary<string, string> Fingerprints,
    IReadOnlyList<ManifestSample> Samples);

/// <summary>
/// Filesystem recording store under a recordings root (default `data/evals/recordings/`).
/// Local-filesystem persistence — port-exempt per Principle I (ADR-011). Writes are
/// wholesale per scenario: a capture run replaces the scenario directory atomically
/// (temp dir + swap), so no mixed capture generations can exist.
/// </summary>
public sealed class RecordingStore
{
    public const string ManifestFileName = "manifest.json";

    private readonly string _root;

    public RecordingStore(string root)
    {
        _root = root;
    }

    public string Root => _root;

    public string ScenarioDirectory(string scenarioId) => Path.Combine(_root, scenarioId);

    public bool HasScenario(string scenarioId)
        => File.Exists(Path.Combine(ScenarioDirectory(scenarioId), ManifestFileName));

    public RecordingManifest LoadManifest(string scenarioId)
    {
        var path = Path.Combine(ScenarioDirectory(scenarioId), ManifestFileName);
        var manifest = JsonSerializer.Deserialize<RecordingManifest>(File.ReadAllText(path), RecordingSerialization.Options)
            ?? throw new InvalidDataException($"Manifest at '{path}' deserialized to null.");

        if (manifest.SchemaVersion != RecordingSerialization.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Manifest at '{path}' has schema_version {manifest.SchemaVersion}; this build understands " +
                $"{RecordingSerialization.CurrentSchemaVersion}. Re-capture the scenario.");
        }

        return manifest;
    }

    public string SamplePath(string scenarioId, string fileName)
        => Path.Combine(ScenarioDirectory(scenarioId), fileName);

    public static string ComputeFileSha256(string path)
        => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>
    /// Write-time credential scan (spec 009 FR-011): a recording that contains the value
    /// of either configured provider credential is rejected before it can reach the store.
    /// </summary>
    private static void RejectCredentialMaterial(string path)
    {
        var content = File.ReadAllText(path);
        foreach (var variable in (string[])["ANTHROPIC_AUTH_TOKEN", "GRIMOIRE_EVAL_PROVIDER_API_KEY"])
        {
            var secret = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(secret) && content.Contains(secret, StringComparison.Ordinal))
            {
                File.Delete(path);
                throw new InvalidOperationException(
                    $"Recording '{Path.GetFileName(path)}' contained the value of {variable} and was rejected (FR-011).");
            }
        }
    }

    /// <summary>
    /// Atomically replaces a scenario's whole recording set: writes manifest + samples to
    /// a temp sibling directory, then swaps it in. Sample hashes in the manifest are
    /// computed from the files as written. Partial scenarios never reach the store —
    /// the caller only invokes this once every sample of the scenario is captured.
    /// </summary>
    public RecordingManifest ReplaceScenario(
        string scenarioId,
        DateTimeOffset capturedAt,
        string model,
        string providerKind,
        IReadOnlyDictionary<string, string> fingerprints,
        IReadOnlyList<RecordedSample> samples)
    {
        Directory.CreateDirectory(_root);
        var finalDir = ScenarioDirectory(scenarioId);
        var tempDir = finalDir + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(tempDir);

        try
        {
            var manifestSamples = new List<ManifestSample>();
            foreach (var sample in samples)
            {
                var fileName = $"sample-{sample.Sample:00}.json";
                var samplePath = Path.Combine(tempDir, fileName);
                RecordingSerialization.Save(samplePath, sample);
                RejectCredentialMaterial(samplePath);
                manifestSamples.Add(new ManifestSample(fileName, ComputeFileSha256(samplePath), sample.TaskId));
            }

            var manifest = new RecordingManifest(
                SchemaVersion: RecordingSerialization.CurrentSchemaVersion,
                ScenarioId: scenarioId,
                CapturedAt: capturedAt,
                Model: model,
                ProviderKind: providerKind,
                Fingerprints: fingerprints,
                Samples: manifestSamples);

            File.WriteAllText(
                Path.Combine(tempDir, ManifestFileName),
                JsonSerializer.Serialize(manifest, RecordingSerialization.Options));

            if (Directory.Exists(finalDir))
            {
                Directory.Delete(finalDir, recursive: true);
            }

            Directory.Move(tempDir, finalDir);
            return manifest;
        }
        catch
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }

            throw;
        }
    }
}
