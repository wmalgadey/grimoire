using Grimoire.EvalRunner.Capture;
using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Grimoire.IngestAgent.AgentCore.Adapters.Replay;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T020/T027 — capture-path hygiene, hermetic: the write-time credential scan rejects
/// recordings containing the configured key (FR-011), and an unreachable provider
/// endpoint yields an actionable connectivity outcome with nothing stored (successor of
/// 007's live-connectivity test; the CLI maps this to exit 2) — never a judgment score.
/// </summary>
[Collection("EvalRunnerProcessTests")]
public class CaptureHygieneTests : IDisposable
{
    private static readonly string[] ProviderEnvKeys =
    [
        "ANTHROPIC_AUTH_TOKEN",
        "GRIMOIRE_EVAL_PROVIDER_BASE_URL",
        "GRIMOIRE_EVAL_PROVIDER_MODEL",
        "GRIMOIRE_EVAL_PROVIDER_API_KEY",
        "GRIMOIRE_INGEST_BASE_URL",
        "GRIMOIRE_INGEST_MODEL",
    ];

    private readonly Dictionary<string, string?> _savedEnv;
    private readonly string _recordingsRoot;

    public CaptureHygieneTests()
    {
        _savedEnv = ProviderEnvKeys.ToDictionary(k => k, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        foreach (var key in ProviderEnvKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        _recordingsRoot = Path.Combine(Path.GetTempPath(), "grimoire-capture-hygiene", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        foreach (var (key, value) in _savedEnv)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            Directory.Delete(_recordingsRoot, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void RecordingStore_RejectsRecordingContainingTheConfiguredCredential()
    {
        const string fakeKey = "nvapi-hygiene-probe-key-0123456789";
        Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", fakeKey);

        var store = new RecordingStore(_recordingsRoot);
        var leakySample = new RecordedSample(
            SchemaVersion: RecordingSerialization.CurrentSchemaVersion,
            Sample: 1,
            TaskId: "hygiene-probe",
            Model: "probe-model",
            Turns:
            [
                new RecordedTurn(1, "sha256:0", [], [], "end_turn", [],
                    AssistantText: $"error mentioning {fakeKey}", InputTokens: 1, OutputTokens: 1),
            ],
            JudgeVerdicts: null,
            Outcome: null);

        var exception = Assert.Throws<InvalidOperationException>(() => store.ReplaceScenario(
            "hygiene-probe",
            DateTimeOffset.UtcNow,
            "probe-model",
            "affordable",
            new Dictionary<string, string>(),
            [leakySample]));

        Assert.Contains("GRIMOIRE_EVAL_PROVIDER_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fakeKey, exception.Message, StringComparison.Ordinal);
        Assert.False(store.HasScenario("hygiene-probe"), "A rejected recording must never reach the store.");
    }

    [Fact]
    public async Task Capture_UnreachableEndpoint_StoresNothing_WithActionableConnectivityOutcome()
    {
        // Nothing listens on port 1 — the child agent's provider call is refused
        // immediately; the run stays hermetic.
        Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:1");
        Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model");
        Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key");

        var gate = EvalProviderResolver.Resolve();
        Assert.Equal(EvalGateStatus.Enabled, gate.Status);

        var paths = EvalPaths.Discover();
        var store = new RecordingStore(_recordingsRoot);
        var pipeline = new CapturePipeline(store, paths, AgentProcessInvoker.ForRepo(paths), NullLogger.Instance);

        var result = await pipeline.RunScenarioAsync(
            ScenarioDefinitions.ConventionAdherence, gate.Configuration, requestedSampleCount: 1, CancellationToken.None);

        Assert.False(result.Stored);
        Assert.False(store.HasScenario("convention-adherence"));
        var sample = Assert.Single(result.Samples);
        Assert.False(sample.Captured);
        Assert.Null(sample.Pass);
        Assert.NotNull(sample.Detail);
    }

    [Fact]
    public void SanitizeErrorText_RedactsBothCredentialSources()
    {
        Environment.SetEnvironmentVariable("GRIMOIRE_EVAL_PROVIDER_API_KEY", "nvapi-redaction-probe");
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "sk-ant-redaction-probe");

        var sanitized = EvalProviderResolver.SanitizeErrorText(
            "failure with nvapi-redaction-probe and sk-ant-redaction-probe embedded");

        Assert.DoesNotContain("nvapi-redaction-probe", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant-redaction-probe", sanitized, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", sanitized, StringComparison.Ordinal);
    }
}
