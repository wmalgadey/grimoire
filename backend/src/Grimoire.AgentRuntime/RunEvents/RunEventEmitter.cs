using System.Text.Json;
using Grimoire.AgentRuntime.Guardrails;

namespace Grimoire.AgentRuntime.RunEvents;

/// <summary>
/// Optional metadata attached to a terminal (<c>completed</c>/<c>failed</c>) event so a
/// harness with no agent-side artifact write path (Grimoire.QueryAgent, ADR-011 R3) can
/// still finalize its own persistent record entirely Hub-side, from the event stream
/// alone. Ingest's call sites leave this null — it writes its own Task Artifact directly
/// and has no need to round-trip this data through the event channel.
/// </summary>
public sealed record RunCompletionMetadata(
    string? SystemPromptSha256 = null,
    string? PolicyPath = null,
    int? PolicyVersion = null,
    string? PolicySha256 = null,
    string? Model = null,
    int? TurnsUsed = null,
    IReadOnlyList<DeniedActionRecord>? DeniedActions = null);

/// <summary>
/// Emits Agent Run Events as NDJSON on stdout (contracts/agent-run-events.md, ADR-008):
/// `started`, periodic `heartbeat` (background timer, independent of model latency),
/// `activity` (loop mechanics only — counters and current action, never wiki-content
/// judgment), `completed` with the final summary, `failed` with a reason. Stdout is a
/// structured protocol surface; all human-readable agent logging goes to stderr/OTLP.
/// </summary>
public sealed class RunEventEmitter : IDisposable
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly TextWriter _writer;
    private readonly string _taskId;
    private readonly Lock _lock = new();
    private Timer? _heartbeatTimer;

    public RunEventEmitter(TextWriter writer, string taskId)
    {
        _writer = writer;
        _taskId = taskId;
    }

    public void EmitStarted()
        => Emit(new { type = "started", taskId = _taskId, timestamp = DateTimeOffset.UtcNow });

    public void StartHeartbeat(TimeSpan interval)
    {
        _heartbeatTimer ??= new Timer(_ => EmitHeartbeat(), null, interval, interval);
    }

    public void EmitHeartbeat()
        => Emit(new { type = "heartbeat", taskId = _taskId, timestamp = DateTimeOffset.UtcNow });

    public void EmitActivity(int modelTurns, int toolCalls, IReadOnlyDictionary<string, int> toolCallsByName, string currentAction)
        => Emit(new
        {
            type = "activity",
            taskId = _taskId,
            timestamp = DateTimeOffset.UtcNow,
            modelTurns,
            toolCalls,
            toolCallsByName,
            currentAction,
        });

    /// <summary>
    /// ADR-011 R2: an incremental streamed-answer delta (contracts/query-run-events.md),
    /// interleaved with `heartbeat`/`activity` on the same NDJSON stdout stream. Emitted
    /// zero or more times per run by agents whose <c>AgentLoop</c> was given an
    /// <c>onTextDelta</c> callback (Grimoire.QueryAgent); Ingest never calls this.
    /// </summary>
    public void EmitAnswerChunk(string text)
        => Emit(new { type = "answer_chunk", taskId = _taskId, timestamp = DateTimeOffset.UtcNow, text });

    public void EmitCompleted(string summary, RunCompletionMetadata? metadata = null)
        => Emit(BuildTerminalPayload("completed", summary, reason: null, metadata));

    public void EmitFailed(string reason, RunCompletionMetadata? metadata = null)
        => Emit(BuildTerminalPayload("failed", summary: null, reason, metadata));

    private object BuildTerminalPayload(string type, string? summary, string? reason, RunCompletionMetadata? metadata)
        => new
        {
            type,
            taskId = _taskId,
            timestamp = DateTimeOffset.UtcNow,
            summary,
            reason,
            systemPromptSha256 = metadata?.SystemPromptSha256,
            policyPath = metadata?.PolicyPath,
            policyVersion = metadata?.PolicyVersion,
            policySha256 = metadata?.PolicySha256,
            model = metadata?.Model,
            turnsUsed = metadata?.TurnsUsed,
            deniedActions = metadata?.DeniedActions?.Select(d => new
            {
                action = d.Action,
                requestedTarget = d.RequestedTarget,
                canonicalTarget = d.CanonicalTarget,
                reason = d.Reason,
                turn = d.Turn,
            }).ToList(),
        };

    private void Emit(object payload)
    {
        var line = JsonSerializer.Serialize(payload, _json);
        lock (_lock)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }
}
