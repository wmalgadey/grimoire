using Grimoire.EvalRunner.Capture;
using Grimoire.EvalRunner.Providers;
using Grimoire.EvalRunner.Recording;
using Grimoire.EvalRunner.Scenarios;
using Grimoire.EvalRunner.Workspace;
using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>
/// T020/T027 — capture-path hygiene, hermetic: the write-time credential scan rejects
/// recordings containing the configured key (FR-011), and an unreachable provider
/// endpoint yields an actionable connectivity outcome with nothing stored (successor of
/// 007's live-connectivity test; the CLI maps this to exit 2) — never a judgment score.
/// Provider configuration is injected per test (#121): these tests never touch the
/// process environment, so they cannot race with classes running in parallel.
/// </summary>
[Trait("Tier", "Fast")]
public class CaptureHygieneTests : IDisposable
{
    private readonly string _recordingsRoot;

    public CaptureHygieneTests()
    {
        _recordingsRoot = Path.Combine(Path.GetTempPath(), "grimoire-capture-hygiene", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
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

        var store = new RecordingStore(_recordingsRoot, Env(("GRIMOIRE_EVAL_PROVIDER_API_KEY", fakeKey)));
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
        var env = Env(
            ("GRIMOIRE_EVAL_PROVIDER_BASE_URL", "http://localhost:1"),
            ("GRIMOIRE_EVAL_PROVIDER_MODEL", "nvidia-model"),
            ("GRIMOIRE_EVAL_PROVIDER_API_KEY", "fake-affordable-key"));

        var gate = EvalProviderResolver.Resolve(env);

        var paths = EvalPaths.Discover();
        var store = new RecordingStore(_recordingsRoot, env);
        var pipeline = new IngestCapturePipeline(store, paths, IngestAgentProcessInvoker.ForRepo(paths, env), NullLogger.Instance);

        var result = await pipeline.RunScenarioAsync(
            IngestScenarioDefinitions.ConventionAdherence, gate.Configuration, requestedSampleCount: 1, CancellationToken.None);

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
        var sanitized = EvalProviderResolver.SanitizeErrorText(
            "failure with nvapi-redaction-probe and sk-ant-redaction-probe embedded",
            Env(
                ("GRIMOIRE_EVAL_PROVIDER_API_KEY", "nvapi-redaction-probe"),
                ("ANTHROPIC_AUTH_TOKEN", "sk-ant-redaction-probe")));

        Assert.DoesNotContain("nvapi-redaction-probe", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant-redaction-probe", sanitized, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", sanitized, StringComparison.Ordinal);
    }

    private static Func<string, string?> Env(params (string Key, string Value)[] entries)
    {
        var map = entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        return key => map.TryGetValue(key, out var value) ? value : null;
    }
}
