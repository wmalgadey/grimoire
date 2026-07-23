using System.Text.Json;

namespace Grimoire.AgentRuntime.RunEvents;

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

    public void EmitCompleted(string summary)
        => Emit(new { type = "completed", taskId = _taskId, timestamp = DateTimeOffset.UtcNow, summary });

    public void EmitFailed(string reason)
        => Emit(new { type = "failed", taskId = _taskId, timestamp = DateTimeOffset.UtcNow, reason });

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
