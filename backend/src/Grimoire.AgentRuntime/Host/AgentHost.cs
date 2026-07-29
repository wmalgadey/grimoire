using Grimoire.AgentRuntime.Instructions;
using Grimoire.AgentRuntime.RunEvents;

namespace Grimoire.AgentRuntime.Host;

/// <summary>
/// Everything the fail-closed startup sequence loaded for this run: the system prompt,
/// the safety policy, and — for profiles requiring the default-user-prompt document —
/// the resolved effective user prompt with its source ("custom" override vs the
/// versioned "default" document, ADR-007).
/// </summary>
public sealed record LoadedInstructions(
    LoadedSystemPrompt SystemPrompt,
    LoadedPolicy Policy,
    string? EffectiveUserPrompt,
    string? UserPromptSource);

/// <summary>
/// The host-side counterpart of the <see cref="AgentProfile"/>: the per-agent intent
/// hooks the platform template calls at its fixed sequence points. Implementations live
/// in the host assemblies and carry the code that differs because the *intent* differs
/// (Ingest: task-artifact lifecycle, ingest-log appending, source reading,
/// rollback/all-denied handling, user-prompt resolution logging; Query: stdin
/// conversation scaffold) — never because the platform was copied (FR-002).
/// </summary>
public interface IAgentIntentHandler
{
    /// <summary>
    /// Pre-instruction-load setup inside the run's try scope (Ingest: create the model
    /// client via ModelClientFactory and write the initial "running" task artifact;
    /// Query: nothing). Exceptions flow to <see cref="DescribeUnhandledFailureAsync"/>.
    /// </summary>
    Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A required instruction document or the policy failed its fail-closed load
    /// (ADR-007). The hook performs the agent's logging/metrics/artifact finalization;
    /// the platform then emits the terminal `failed` event with the same reason and
    /// exits 1. <paramref name="documentKind"/> is the frozen per-agent failure kind
    /// ("instructions", "default_user_prompt", "policy").
    /// </summary>
    Task OnInstructionLoadFailureAsync(string documentKind, string documentPath, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Instructions and policy loaded successfully — the hook emits the agent's frozen
    /// instructions-loaded log events and its `*.load_instructions` span (block-scoped,
    /// so later model_turn spans parent to the run span, not to load_instructions).
    /// </summary>
    Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken);

    /// <summary>
    /// The agent's run body, invoked after `started` + heartbeat are live: build the
    /// guarded executor and AgentLoop from the profile's tool registry, run it, and
    /// emit the agent's terminal event(s). Returns the process exit code.
    /// </summary>
    Task<int> ExecuteAsync(LoadedInstructions instructions, CancellationToken cancellationToken);

    /// <summary>
    /// An exception escaped the sequence (including <see cref="ExecuteAsync"/>): the
    /// hook performs the agent's failure handling (logging, rollback, artifact
    /// finalization) and returns the — already credential-sanitized where required —
    /// reason text; the platform then emits the terminal `failed` event and exits 1.
    /// </summary>
    Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken);
}

/// <summary>
/// The one startup/shutdown template for agent host processes (ADR-013; consolidates
/// the formerly duplicated inline sequencing of both hosts' Program.cs): fail-closed
/// instruction + policy load (existing SystemPromptLoader/PolicyLoader, unchanged) →
/// `started` event → heartbeat → agent body (AgentLoop) → terminal event. The ADR-008/
/// ADR-011 event sequencing is byte-compatible; per-agent behavior enters exclusively
/// through the <see cref="AgentProfile"/> and <see cref="IAgentIntentHandler"/> hooks —
/// no agent-conditional branches exist anywhere in the platform (FR-002).
/// </summary>
public sealed class AgentHost
{
    private readonly AgentProfile _profile;

    public AgentHost(AgentProfile profile)
    {
        _profile = profile;
    }

    /// <summary>
    /// Runs the startup/shutdown template. <paramref name="runEvents"/> is created by
    /// the composition root (stdout is the NDJSON event channel, ADR-008) together with
    /// the run span, both before this call — exactly as before the consolidation.
    /// </summary>
    public async Task<int> RunAsync(
        AgentHostRun run,
        RunEventEmitter runEvents,
        IAgentIntentHandler intent,
        CancellationToken cancellationToken)
    {
        try
        {
            await intent.PrepareAsync(cancellationToken);

            var promptLoader = new SystemPromptLoader();
            var systemPromptResult = await promptLoader.LoadAsync(run.SystemPromptPath, cancellationToken);
            if (systemPromptResult.IsSecond(out var systemPromptFailure))
            {
                await intent.OnInstructionLoadFailureAsync(
                    "instructions", run.SystemPromptPath, systemPromptFailure.Reason, cancellationToken);
                runEvents.EmitFailed(systemPromptFailure.Reason);
                return 1;
            }
            systemPromptResult.IsFirst(out var loadedSystemPrompt);

            // Effective user prompt (profiles requiring the default-user-prompt
            // document, ADR-007): explicit --user-prompt override, else the versioned
            // default document. No override + missing/empty default ⇒ fail closed.
            string? effectiveUserPrompt = null;
            string? userPromptSource = null;
            if (_profile.RequiredInstructionDocuments.Contains(InstructionDocument.DefaultUserPrompt))
            {
                if (!string.IsNullOrWhiteSpace(run.UserPromptOverride))
                {
                    effectiveUserPrompt = run.UserPromptOverride.Trim();
                    userPromptSource = "custom";
                }
                else
                {
                    var defaultPromptResult = await promptLoader.LoadAsync(run.DefaultUserPromptPath!, cancellationToken);
                    if (defaultPromptResult.IsSecond(out var defaultPromptFailure))
                    {
                        await intent.OnInstructionLoadFailureAsync(
                            "default_user_prompt", run.DefaultUserPromptPath!, defaultPromptFailure.Reason, cancellationToken);
                        runEvents.EmitFailed(defaultPromptFailure.Reason);
                        return 1;
                    }
                    defaultPromptResult.IsFirst(out var loadedDefaultPrompt);
                    effectiveUserPrompt = loadedDefaultPrompt!.Content.Trim();
                    userPromptSource = "default";
                }
            }

            var policyLoader = new PolicyLoader(run.WikiRoot);
            var policyResult = await policyLoader.LoadAsync(run.PolicyPath, cancellationToken);
            if (policyResult.IsSecond(out var policyFailure))
            {
                await intent.OnInstructionLoadFailureAsync(
                    "policy", run.PolicyPath, policyFailure.Reason, cancellationToken);
                runEvents.EmitFailed(policyFailure.Reason);
                return 1;
            }
            policyResult.IsFirst(out var loadedPolicy);

            var instructions = new LoadedInstructions(
                loadedSystemPrompt!, loadedPolicy!, effectiveUserPrompt, userPromptSource);
            await intent.OnInstructionsLoadedAsync(instructions, cancellationToken);

            // Event channel goes live once instructions and policy are loaded (contract:
            // started first, then heartbeats independent of model latency — ADR-008).
            runEvents.EmitStarted();
            runEvents.StartHeartbeat(TimeSpan.FromSeconds(run.HeartbeatSeconds));

            return await intent.ExecuteAsync(instructions, cancellationToken);
        }
        catch (Exception ex)
        {
            var reason = await intent.DescribeUnhandledFailureAsync(ex, cancellationToken);
            runEvents.EmitFailed(reason);
            return 1;
        }
    }
}

/// <summary>
/// The per-run inputs of the startup template, mapped by the composition root from its
/// CLI options (paths keep flowing Hub→CLI per ADR-009; the platform performs no
/// ambient discovery).
/// </summary>
public sealed record AgentHostRun(
    string WikiRoot,
    string SystemPromptPath,
    string PolicyPath,
    int HeartbeatSeconds,
    string? DefaultUserPromptPath = null,
    string? UserPromptOverride = null);
