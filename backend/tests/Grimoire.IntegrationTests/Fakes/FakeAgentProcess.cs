using System.Text.Json;
using System.Threading.Channels;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.RemediationTasks;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Hermetic stand-in for the agent child process (T033, Test Strategy SC-008/SC-009):
/// an <see cref="IAgentProcessHandle"/> whose stdout lines are scripted by the test via
/// a channel — event sequences, silence, malformed lines, and pipe-close without a
/// terminal event are all expressible without spawning a real process.
/// </summary>
// 019-fast-test-tier (ADR-021 R4): EmitAnswerChunksAsync's scripted per-chunk delay
// simulates the production streaming timing tests assert against (SC-003 latency budgets)
// — it is the behavior under test's own timing, not a wait for an unrelated async op.
[Trait("TimingDependent", "true")]
public sealed class ScriptedAgentProcessHandle : IAgentProcessHandle
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public bool Terminated { get; private set; }

    /// <summary>
    /// 023-task-ui-improvements T012: true once supervision has attached to this handle's
    /// stdout. Tests that drive virtual time wait on it — the coordinator arms its liveness
    /// watchdog immediately before starting the read loop, so an attached read loop proves
    /// the watchdog timer is registered and a <c>FakeTimeProvider.Advance</c> can no longer
    /// land before it exists (which would silently arm nothing and hang the test).
    /// </summary>
    public bool ReadLoopAttached { get; private set; }

    public void EmitLine(string line) => _lines.Writer.TryWrite(line);

    public void EmitEvent(string type, string taskId, object? extra = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["taskId"] = taskId,
            ["timestamp"] = DateTimeOffset.UtcNow,
        };
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties())
            {
                payload[property.Name] = property.GetValue(extra);
            }
        }

        EmitLine(JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// T011 (011-query-conversations): dictionary-shaped variant of <see cref="EmitEvent"/>
    /// so callers can merge scripted terminal-event metadata (denied actions, instruction/
    /// policy identity, model, turns used) into one event payload deterministically.
    /// </summary>
    public void EmitEventWithFields(string type, string taskId, IReadOnlyDictionary<string, object?> fields)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["taskId"] = taskId,
            ["timestamp"] = DateTimeOffset.UtcNow,
        };
        foreach (var (key, value) in fields)
        {
            payload[key] = value;
        }

        EmitLine(JsonSerializer.Serialize(payload));
    }

    /// <summary>Closes the stdout pipe without a terminal event (process exit / crash).</summary>
    public void ClosePipe() => _lines.Writer.TryComplete();

    /// <summary>
    /// T025 (008-query-agent): scripts a sequence of <c>answer_chunk</c> events
    /// (contracts/query-run-events.md) for one turn, with an optional delay before each
    /// so SC-003 timing scenarios (first chunk immediate, later chunks delayed) can be
    /// driven deterministically without a live LLM call.
    /// </summary>
    public async Task EmitAnswerChunksAsync(
        string taskId,
        IEnumerable<(string Text, TimeSpan Delay)> chunks,
        CancellationToken cancellationToken = default)
    {
        foreach (var (text, delay) in chunks)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            EmitEvent("answer_chunk", taskId, new { text });
        }
    }

    public async IAsyncEnumerable<string> ReadStdoutLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ReadLoopAttached = true;
        await foreach (var line in _lines.Reader.ReadAllAsync(cancellationToken))
        {
            yield return line;
        }
    }

    public void Terminate()
    {
        Terminated = true;
        _lines.Writer.TryComplete();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Test double for <see cref="IAgentProcessLauncher"/>: records every request and, in
/// auto-play mode, simulates the agent's own behavior (running + terminal artifact
/// writes, `started` + terminal events) without a real child process or credentials.
/// Pass <c>autoPlay: false</c> to script the handle manually (supervision tests).
/// </summary>
// 019-fast-test-tier (ADR-021 R4): _simulatedRunDuration models the real agent process's
// own run-window timing (tests assert non-overlapping windows, FIFO ordering, etc. against
// it) — it is the behavior under test's own timing, not a wait for an unrelated async op.
[Trait("TimingDependent", "true")]
public sealed class FakeAgentProcessLauncher : IAgentProcessLauncher
{
    private readonly string _terminalStatus;
    private readonly string? _failureReason;
    private readonly TimeSpan _simulatedRunDuration;
    private readonly Exception? _throwOnStart;
    private readonly bool _autoPlay;

    public List<IngestAgentRequest> Requests { get; } = [];
    public List<(DateTimeOffset Started, DateTimeOffset Finished)> RunWindows { get; } = [];
    public List<ScriptedAgentProcessHandle> Handles { get; } = [];

    private int _ingestLaunchCount;

    /// <summary>
    /// 023-task-ui-improvements T002 (US2, FR-007/SC-005): go-silent mode. The first
    /// <see cref="GoSilentIngestLaunches"/> ingest launches write the `running` artifact and
    /// emit `started` — exactly what a real agent does — and then fall silent: no further
    /// events, no terminal event, pipe left open. That is precisely the liveness-window
    /// trigger the coordinator's reactivation path recovers from. Launches beyond the count
    /// behave per <c>autoPlay</c>, so a test can script "silent for N attempts, then a
    /// normal run" as well as "silent forever" (<c>int.MaxValue</c>).
    /// </summary>
    public int GoSilentIngestLaunches { get; set; }

    /// <summary>Every <see cref="QueryAgentRequest"/> received via the Query-shaped StartAsync overload.</summary>
    public List<QueryAgentRequest> QueryRequests { get; } = [];

    /// <summary>Every <see cref="LintAgentRequest"/> received via the Lint-shaped StartAsync overload.</summary>
    public List<LintAgentRequest> LintRequests { get; } = [];

    public FakeAgentProcessLauncher(
        string terminalStatus = "completed",
        string? failureReason = null,
        TimeSpan? simulatedRunDuration = null,
        Exception? throwOnStart = null,
        bool autoPlay = true)
    {
        _terminalStatus = terminalStatus;
        _failureReason = failureReason;
        _simulatedRunDuration = simulatedRunDuration ?? TimeSpan.Zero;
        _throwOnStart = throwOnStart;
        _autoPlay = autoPlay;
    }

    public async Task<IAgentProcessHandle> StartAsync(IngestAgentRequest request, CancellationToken cancellationToken = default)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        if (_throwOnStart is not null)
        {
            throw _throwOnStart;
        }

        var handle = new ScriptedAgentProcessHandle();
        int launchIndex;
        lock (Handles)
        {
            Handles.Add(handle);
            launchIndex = ++_ingestLaunchCount;
        }

        if (launchIndex <= GoSilentIngestLaunches)
        {
            // Go silent (T002): the run visibly starts and then stops emitting anything.
            await WriteArtifactAsync(Path.Combine(request.TasksDir, $"{request.TaskId}.md"), request, "running", null);
            handle.EmitEvent("started", request.TaskId);
            return handle;
        }

        if (_autoPlay)
        {
            var started = DateTimeOffset.UtcNow;
            var taskArtifactPath = Path.Combine(request.TasksDir, $"{request.TaskId}.md");
            await WriteArtifactAsync(taskArtifactPath, request, "running", null);
            handle.EmitEvent("started", request.TaskId);

            _ = Task.Run(async () =>
            {
                if (_simulatedRunDuration > TimeSpan.Zero)
                {
                    await Task.Delay(_simulatedRunDuration, CancellationToken.None);
                }

                await WriteArtifactAsync(taskArtifactPath, request, _terminalStatus, _failureReason);

                // The window must be committed before the completion event/pipe close:
                // the coordinator may dispatch the next run the moment it observes the
                // terminal signal, and a Finished stamp taken after that handoff can
                // postdate the next run's Started, breaking the non-overlap assertions.
                lock (RunWindows)
                {
                    RunWindows.Add((started, DateTimeOffset.UtcNow));
                }

                if (_terminalStatus == "failed")
                {
                    handle.EmitEvent("failed", request.TaskId, new { reason = _failureReason ?? "Fake agent run failed." });
                }
                else
                {
                    handle.EmitEvent("completed", request.TaskId, new { summary = "Fake agent run completed." });
                }

                handle.ClosePipe();
            }, CancellationToken.None);
        }

        return handle;
    }

    /// <summary>
    /// Query-shaped StartAsync (ADR-011): records the request and, in auto-play mode,
    /// scripts `started` → optional `answer_chunk` deltas (<see cref="ScriptedAnswerChunks"/>)
    /// → terminal event, without writing any artifact (Query has no write path at all).
    /// </summary>
    public IReadOnlyList<(string Text, TimeSpan Delay)>? ScriptedAnswerChunks { get; set; }

    /// <summary>
    /// T011 (011-query-conversations): optional terminal-event metadata merged into the
    /// auto-play query terminal event (ADR-006/ADR-011 terminal metadata: instruction
    /// identity + sha256, policy identity/version/sha256, model, turns used, denied
    /// actions) — camelCase keys exactly as <c>AgentRunEvent</c> deserializes them, e.g.
    /// <c>["systemPromptSha256"] = "abc"</c>, <c>["deniedActions"] = new[] { ... }</c>.
    /// When unset, the emitted events are byte-for-byte what they were before this hook.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ScriptedQueryTerminalMetadata { get; set; }

    public Task<IAgentProcessHandle> StartAsync(QueryAgentRequest request, CancellationToken cancellationToken = default)
    {
        lock (QueryRequests)
        {
            QueryRequests.Add(request);
        }

        if (_throwOnStart is not null)
        {
            throw _throwOnStart;
        }

        var handle = new ScriptedAgentProcessHandle();
        lock (Handles)
        {
            Handles.Add(handle);
        }

        if (_autoPlay)
        {
            handle.EmitEvent("started", request.TurnId);

            _ = Task.Run(async () =>
            {
                if (_simulatedRunDuration > TimeSpan.Zero)
                {
                    await Task.Delay(_simulatedRunDuration, CancellationToken.None);
                }

                if (ScriptedAnswerChunks is { Count: > 0 })
                {
                    await handle.EmitAnswerChunksAsync(request.TurnId, ScriptedAnswerChunks, CancellationToken.None);
                }

                if (ScriptedQueryTerminalMetadata is { } metadata)
                {
                    var fields = new Dictionary<string, object?>(metadata.ToDictionary(kv => kv.Key, kv => kv.Value));
                    if (_terminalStatus == "failed")
                    {
                        fields.TryAdd("reason", _failureReason ?? "Fake query run failed.");
                        handle.EmitEventWithFields("failed", request.TurnId, fields);
                    }
                    else
                    {
                        fields.TryAdd("summary", "Fake query run completed.");
                        handle.EmitEventWithFields("completed", request.TurnId, fields);
                    }
                }
                else if (_terminalStatus == "failed")
                {
                    handle.EmitEvent("failed", request.TurnId, new { reason = _failureReason ?? "Fake query run failed." });
                }
                else
                {
                    handle.EmitEvent("completed", request.TurnId, new { summary = "Fake query run completed." });
                }

                handle.ClosePipe();
            }, CancellationToken.None);
        }

        return Task.FromResult<IAgentProcessHandle>(handle);
    }

    /// <summary>
    /// T015 (013-lint-agent): optional terminal-event metadata merged into the auto-play
    /// lint terminal event (instruction/policy identity, model, turns used, denied
    /// actions, touched/refreshed paths via the reused <c>createdPages</c> wire field) —
    /// camelCase keys exactly as <c>AgentRunEvent</c> deserializes them. When unset, the
    /// emitted events are byte-for-byte what they were before this hook.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ScriptedLintTerminalMetadata { get; set; }

    /// <summary>ADR-016 (013-lint-agent): Lint-shaped StartAsync — no stdin payload at all
    /// (Lint takes no per-run input beyond the wiki itself).</summary>
    public Task<IAgentProcessHandle> StartAsync(LintAgentRequest request, CancellationToken cancellationToken = default)
    {
        lock (LintRequests)
        {
            LintRequests.Add(request);
        }

        if (_throwOnStart is not null)
        {
            throw _throwOnStart;
        }

        var handle = new ScriptedAgentProcessHandle();
        lock (Handles)
        {
            Handles.Add(handle);
        }

        if (_autoPlay)
        {
            handle.EmitEvent("started", request.RunId);

            _ = Task.Run(async () =>
            {
                if (_simulatedRunDuration > TimeSpan.Zero)
                {
                    await Task.Delay(_simulatedRunDuration, CancellationToken.None);
                }

                if (ScriptedLintTerminalMetadata is { } metadata)
                {
                    var fields = new Dictionary<string, object?>(metadata.ToDictionary(kv => kv.Key, kv => kv.Value));
                    if (_terminalStatus == "failed")
                    {
                        fields.TryAdd("reason", _failureReason ?? "Fake lint run failed.");
                        handle.EmitEventWithFields("failed", request.RunId, fields);
                    }
                    else
                    {
                        fields.TryAdd("summary", "Fake lint run completed.");
                        handle.EmitEventWithFields("completed", request.RunId, fields);
                    }
                }
                else if (_terminalStatus == "failed")
                {
                    handle.EmitEvent("failed", request.RunId, new { reason = _failureReason ?? "Fake lint run failed." });
                }
                else
                {
                    handle.EmitEvent("completed", request.RunId, new { summary = "Fake lint run completed." });
                }

                handle.ClosePipe();
            }, CancellationToken.None);
        }

        return Task.FromResult<IAgentProcessHandle>(handle);
    }

    /// <summary>Every <see cref="RemediationExecutionAgentRequest"/> received via the
    /// remediation-shaped StartAsync overload (015-lint-board-parity T030, SC-005: tests
    /// assert this stays empty for every non-Authorized-dispatch attempt).</summary>
    public List<RemediationExecutionAgentRequest> RemediationRequests { get; } = [];

    /// <summary>
    /// 015-lint-board-parity T030: optional terminal-event metadata merged into the
    /// auto-play remediation-execution terminal event (camelCase keys exactly as
    /// <c>AgentRunEvent</c> deserializes them, e.g. <c>["remediationOutcome"] =
    /// "not_applicable"</c>, <c>["reason"] = "..."</c>). When unset, the emitted events are
    /// byte-for-byte what they were before this hook (plain completed/failed).
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ScriptedRemediationTerminalMetadata { get; set; }

    /// <summary>ADR-018 (015-lint-board-parity): remediation-execution-shaped StartAsync — no
    /// stdin payload, mirroring Lint's own convention (the request already carries the
    /// proposal verbatim).</summary>
    public Task<IAgentProcessHandle> StartAsync(RemediationExecutionAgentRequest request, CancellationToken cancellationToken = default)
    {
        lock (RemediationRequests)
        {
            RemediationRequests.Add(request);
        }

        if (_throwOnStart is not null)
        {
            throw _throwOnStart;
        }

        var handle = new ScriptedAgentProcessHandle();
        lock (Handles)
        {
            Handles.Add(handle);
        }

        if (_autoPlay)
        {
            handle.EmitEvent("started", request.TaskId);

            _ = Task.Run(async () =>
            {
                if (_simulatedRunDuration > TimeSpan.Zero)
                {
                    await Task.Delay(_simulatedRunDuration, CancellationToken.None);
                }

                if (ScriptedRemediationTerminalMetadata is { } metadata)
                {
                    var fields = new Dictionary<string, object?>(metadata.ToDictionary(kv => kv.Key, kv => kv.Value));
                    if (_terminalStatus == "failed")
                    {
                        fields.TryAdd("reason", _failureReason ?? "Fake remediation run failed.");
                        handle.EmitEventWithFields("failed", request.TaskId, fields);
                    }
                    else
                    {
                        fields.TryAdd("summary", "Fake remediation run completed.");
                        handle.EmitEventWithFields("completed", request.TaskId, fields);
                    }
                }
                else if (_terminalStatus == "failed")
                {
                    handle.EmitEvent("failed", request.TaskId, new { reason = _failureReason ?? "Fake remediation run failed." });
                }
                else
                {
                    handle.EmitEvent("completed", request.TaskId, new { summary = "Fake remediation run completed." });
                }

                handle.ClosePipe();
            }, CancellationToken.None);
        }

        return Task.FromResult<IAgentProcessHandle>(handle);
    }

    /// <summary>Every <see cref="RemediationMessageTurnAgentRequest"/> received via the
    /// message-turn-shaped StartAsync overload (015-lint-board-parity T040).</summary>
    public List<RemediationMessageTurnAgentRequest> MessageTurnRequests { get; } = [];

    /// <summary>
    /// The agent's reply text in auto-play mode's completed event `text` field
    /// (contracts/remediation-lifecycle-events.md "Message-turn mode terminal event").
    /// </summary>
    public string ScriptedMessageTurnReply { get; set; } = "Fake message-turn reply.";

    /// <summary>ADR-018 (015-lint-board-parity T042): message-turn-shaped StartAsync —
    /// stdin carries the message/priorMessages JSON payload, mirroring the real
    /// AgentProcessHost, but the fake never actually reads it (no real stdin pipe here).</summary>
    public Task<IAgentProcessHandle> StartAsync(RemediationMessageTurnAgentRequest request, CancellationToken cancellationToken = default)
    {
        lock (MessageTurnRequests)
        {
            MessageTurnRequests.Add(request);
        }

        if (_throwOnStart is not null)
        {
            throw _throwOnStart;
        }

        var handle = new ScriptedAgentProcessHandle();
        lock (Handles)
        {
            Handles.Add(handle);
        }

        if (_autoPlay)
        {
            handle.EmitEvent("started", request.TaskId);

            _ = Task.Run(async () =>
            {
                if (_simulatedRunDuration > TimeSpan.Zero)
                {
                    await Task.Delay(_simulatedRunDuration, CancellationToken.None);
                }

                if (_terminalStatus == "failed")
                {
                    handle.EmitEvent("failed", request.TaskId, new { reason = _failureReason ?? "Fake message-turn run failed." });
                }
                else
                {
                    handle.EmitEvent("completed", request.TaskId, new { summary = ScriptedMessageTurnReply, text = ScriptedMessageTurnReply });
                }

                handle.ClosePipe();
            }, CancellationToken.None);
        }

        return Task.FromResult<IAgentProcessHandle>(handle);
    }

    /// <summary>
    /// Manual CLI path test double: mirrors the auto-play artifact write without a
    /// scripted handle/event stream (SubmissionService only calls this method, never
    /// StartAsync). Returns 0 (success) unless <c>throwOnStart</c> was configured.
    /// </summary>
    public async Task<int> RunToExitAsync(IngestAgentRequest request, CancellationToken cancellationToken = default)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        if (_throwOnStart is not null)
        {
            throw _throwOnStart;
        }

        var taskArtifactPath = Path.Combine(request.TasksDir, $"{request.TaskId}.md");
        await WriteArtifactAsync(taskArtifactPath, request, _terminalStatus, _failureReason);
        return _terminalStatus == "failed" ? 1 : 0;
    }

    private static async Task WriteArtifactAsync(string path, IngestAgentRequest request, string status, string? failureReason)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var failure = failureReason is null ? "null" : $"\"{failureReason}\"";
        var userPromptSource = request.UserPrompt is null ? "default" : "custom";

        // Mirrors the real agent's Program.cs behavior (004 FR-014): convert-step
        // configuration is Hub-owned, so carry forward whatever the Hub already wrote
        // instead of dropping it when this fake "takes over" the artifact.
        var convertStepsLine = "null";
        if (File.Exists(path))
        {
            var existing = Grimoire.Hub.IngestSubmission.TaskArtifactFrontmatter.TryParse(await File.ReadAllTextAsync(path));
            if (existing?.ConvertSteps is { Count: > 0 } steps)
            {
                var entries = steps.OrderBy(s => s.Key, StringComparer.Ordinal)
                    .Select(s => $"\"{s.Key}\": {(s.Value ? "enabled" : "disabled")}");
                convertStepsLine = "{" + string.Join(", ", entries) + "}";
            }
        }

        var content =
            $"""
            ---
            task_id: {request.TaskId}
            type: ingest
            status: {status}
            agent: ingest
            started_at: {DateTimeOffset.UtcNow:O}
            completed_at: {(status is "completed" or "failed" ? DateTimeOffset.UtcNow.ToString("O") : "null")}
            source_ref: "{request.SourceRef}"
            pages_touched: []
            pages_created: []
            pages_updated: []
            pages_superseded: []
            denied_actions: []
            instruction_files: []
            policy: null
            model: null
            turns: null
            rolled_back: null
            user_prompt_source: {userPromptSource}
            convert_steps: {convertStepsLine}
            failure_reason: {failure}
            ---

            Fake agent run ({status}).
            """;

        // Atomic temp-file + rename, mirroring HubTaskArtifactWriter.WriteAsync: a reader
        // (KanbanBoardProjectionStore) may be concurrently polling this same path, and an
        // in-place File.WriteAllTextAsync can hand it a torn read (truncated-then-partially-
        // written content) that fails TryParse and looks like "file gone" — rare under full
        // suite serialization, reachable once 019 (ADR-021) enabled collection parallelization.
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }
}
