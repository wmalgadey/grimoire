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
    IReadOnlyList<DeniedActionRecord>? DeniedActions = null,
    // ADR-015 (012-query-synthesis-writes): canonical paths from this run's
    // GuardedToolExecutor.CreatedPaths (create-only writes that succeeded) — mechanical
    // reporting of what the run's own journal already recorded, no content judgment
    // (Constitution Principle V). Null/empty for a turn that created nothing.
    IReadOnlyList<string>? CreatedArtifacts = null,
    // ADR-018 (015-lint-board-parity T025): remediation actions the Lint agent judged
    // actionable, carried verbatim onto the lint-run terminal event
    // (contracts/remediation-lifecycle-events.md `proposedActions`). Pure transport —
    // the judgment lives in the agent's instructions, never here (Principle V).
    // Null/empty for a run that proposed nothing; only Lint's lint-run mode sets it.
    IReadOnlyList<ProposedActionRecord>? ProposedActions = null,
    // 028-lint-at-scale (US2, FR-003): the lint-run mode's harness-computed coverage
    // report (contracts/coverage-signal.md `wiki_coverage`). Null for every other
    // agent/mode; only Lint's lint-run mode sets it.
    WikiCoverage? WikiCoverage = null,
    // T035 (015-lint-board-parity, ADR-018, FR-018): the remediation-execution mode's
    // re-verification judgment on its terminal `completed` event — "applied" |
    // "not_applicable" (contracts/remediation-lifecycle-events.md). Transported only,
    // never computed by the harness (Principle V); only the remediation-execution mode
    // sets it. The accompanying reason travels via <see cref="RunEventEmitter.EmitCompleted"/>'s
    // own <c>reason</c> parameter, mirroring <c>EmitFailed</c>'s shape.
    string? RemediationOutcome = null);

/// <summary>
/// One agent-proposed remediation action as reported on the lint-run terminal event
/// (015-lint-board-parity, ADR-008/ADR-018 event-vocabulary extension). All three
/// fields are agent-authored free text, harness-opaque; <see cref="TargetPath"/> is an
/// optional hint the Hub never validates or enforces.
/// </summary>
public sealed record ProposedActionRecord(string Title, string Description, string? TargetPath = null);

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
    // Issue #184: a monotonically increasing counter of harness-observed loop mechanics,
    // carried on `heartbeat` events (contracts/agent-run-events.md). Bumped by
    // RecordProgress — called from AgentLoop for every streamed text delta and internally
    // by EmitActivity for every completed turn/dispatched tool call — never by content
    // judgment (Constitution Principle V: pure mechanics, like the modelTurns/toolCalls
    // counters `activity` already reports).
    private long _progress;

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

    /// <summary>
    /// Issue #184: records one unit of loop-mechanical forward progress — a streamed text
    /// delta, a completed model turn, a dispatched tool call. Thread-safe (the background
    /// heartbeat timer reads it concurrently with the agent loop's own thread writing it).
    /// </summary>
    public void RecordProgress() => Interlocked.Increment(ref _progress);

    public void EmitHeartbeat()
        => Emit(new { type = "heartbeat", taskId = _taskId, timestamp = DateTimeOffset.UtcNow, progress = Interlocked.Read(ref _progress) });

    public void EmitActivity(int modelTurns, int toolCalls, IReadOnlyDictionary<string, int> toolCallsByName, string currentAction)
    {
        // Every activity event reports a completed loop step (a model turn finished, a
        // tool call dispatched) — genuine forward progress, not merely time passing.
        RecordProgress();
        Emit(new
        {
            type = "activity",
            taskId = _taskId,
            timestamp = DateTimeOffset.UtcNow,
            modelTurns,
            toolCalls,
            toolCallsByName,
            currentAction,
        });
    }

    /// <summary>
    /// ADR-011 R2: an incremental streamed-answer delta (contracts/query-run-events.md),
    /// interleaved with `heartbeat`/`activity` on the same NDJSON stdout stream. Emitted
    /// zero or more times per run by agents whose <c>AgentLoop</c> was given an
    /// <c>onTextDelta</c> callback (Grimoire.QueryAgent); Ingest never calls this.
    /// </summary>
    public void EmitAnswerChunk(string text)
        => Emit(new { type = "answer_chunk", taskId = _taskId, timestamp = DateTimeOffset.UtcNow, text });

    // T035 (015-lint-board-parity): completed events can now carry a `reason` too — the
    // remediation-execution mode's not-applicable outcome needs one alongside `summary`
    // (contracts/remediation-lifecycle-events.md), unlike every prior agent whose
    // completed events never had one. Defaults to null so every existing call site
    // (Ingest/Query/Lint's lint-run mode) is unaffected.
    // T042 (015-lint-board-parity, ADR-018): also accepts an optional `text` — the
    // message-turn mode's bounded reply, carried on the existing `text` field per
    // contracts/remediation-lifecycle-events.md "Message-turn mode terminal event" (no
    // new event field; reused from `answer_chunk`'s `text`). Null for every other mode.
    public void EmitCompleted(string summary, RunCompletionMetadata? metadata = null, string? reason = null, string? text = null)
        => Emit(BuildTerminalPayload("completed", summary, reason, metadata, text));

    public void EmitFailed(string reason, RunCompletionMetadata? metadata = null)
        => Emit(BuildTerminalPayload("failed", summary: null, reason, metadata, text: null));

    private object BuildTerminalPayload(string type, string? summary, string? reason, RunCompletionMetadata? metadata, string? text)
        => new
        {
            type,
            taskId = _taskId,
            timestamp = DateTimeOffset.UtcNow,
            summary,
            reason,
            text,
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
            createdPages = metadata?.CreatedArtifacts,
            // ADR-018 (015-lint-board-parity): rides the terminal event like
            // deniedActions/createdPages above; null when the run proposed nothing.
            proposedActions = metadata?.ProposedActions?.Select(p => new
            {
                title = p.Title,
                description = p.Description,
                targetPath = p.TargetPath,
            }).ToList(),
            // 028-lint-at-scale (US2, FR-003): rides the terminal event like
            // deniedActions/createdPages/proposedActions above; null for every run that
            // did not compute a coverage report.
            wikiCoverage = metadata?.WikiCoverage is { } coverage
                ? new { pagesTotal = coverage.PagesTotal, pagesConsidered = coverage.PagesConsidered, status = coverage.Status }
                : null,
            // T035 (015-lint-board-parity, ADR-018): remediation-execution mode's
            // re-verification outcome, null for every other agent/mode.
            remediationOutcome = metadata?.RemediationOutcome,
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
