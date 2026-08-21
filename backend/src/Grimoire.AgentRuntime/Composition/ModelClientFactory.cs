using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Core.Adapters.Replay;
using Microsoft.Extensions.Logging;

namespace Grimoire.AgentRuntime.Composition;

/// <summary>
/// The per-agent model/base-url/output-ceiling environment-variable names (ADR-004
/// credential/model scoping): Ingest keeps the adapter defaults (GRIMOIRE_INGEST_MODEL /
/// GRIMOIRE_INGEST_BASE_URL / GRIMOIRE_INGEST_MAX_OUTPUT_TOKENS), Query and Lint supply
/// their own GRIMOIRE_QUERY_* and GRIMOIRE_LINT_* names. A frozen profile input —
/// consolidation must not merge the agents' credential/config scopes. The output ceiling
/// (#122) joins the set because it is per-agent for the same reason the model is: what
/// Query needs to emit for one answer and what Ingest needs for a full wiki page are not
/// the same number, and neither is a property of the code.
/// </summary>
public sealed record ModelEnvVarNames(
    string ModelEnvVar,
    string BaseUrlEnvVar,
    string MaxOutputTokensEnvVar);

/// <summary>
/// The single implementation of ADR-012's composition-root model-adapter selection
/// (ADR-013 D2; consolidates the formerly duplicated CreateModelClient in both hosts'
/// Program.cs byte-compatibly): GRIMOIRE_MODEL_REPLAY_PATH serves a recording with no
/// credential read; GRIMOIRE_MODEL_CAPTURE_PATH wraps the live adapter in the
/// turn-capture decorator; both set is a fail-fast configuration error; neither
/// preserves production behavior unchanged. Still invoked from each host's composition
/// root, with the profile supplying the per-agent env-var names.
/// </summary>
public static class ModelClientFactory
{
    public static IModelClient Create(ILoggerFactory loggerFactory, ModelEnvVarNames modelEnvVarNames)
    {
        var replayPath = Environment.GetEnvironmentVariable("GRIMOIRE_MODEL_REPLAY_PATH");
        var capturePath = Environment.GetEnvironmentVariable("GRIMOIRE_MODEL_CAPTURE_PATH");

        if (!string.IsNullOrWhiteSpace(replayPath) && !string.IsNullOrWhiteSpace(capturePath))
        {
            throw new InvalidOperationException(
                "Both GRIMOIRE_MODEL_REPLAY_PATH and GRIMOIRE_MODEL_CAPTURE_PATH are set. " +
                "Configure at most one of replay/capture mode (ADR-012); production leaves both unset.");
        }

        if (!string.IsNullOrWhiteSpace(replayPath))
        {
            return new ReplayModelClient(replayPath);
        }

        var liveClient = new AnthropicModelClient(
            loggerFactory.CreateLogger<AnthropicModelClient>(),
            modelEnvVar: modelEnvVarNames.ModelEnvVar,
            baseUrlEnvVar: modelEnvVarNames.BaseUrlEnvVar,
            maxOutputTokensEnvVar: modelEnvVarNames.MaxOutputTokensEnvVar);
        return string.IsNullOrWhiteSpace(capturePath)
            ? liveClient
            : new TurnCaptureModelClient(liveClient, capturePath);
    }
}
