using System.Text.Json;
using System.Threading.Channels;
using Grimoire.Hub.AgentDispatch;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Hermetic stand-in for the agent child process (T033, Test Strategy SC-008/SC-009):
/// an <see cref="IAgentProcessHandle"/> whose stdout lines are scripted by the test via
/// a channel — event sequences, silence, malformed lines, and pipe-close without a
/// terminal event are all expressible without spawning a real process.
/// </summary>
public sealed class ScriptedAgentProcessHandle : IAgentProcessHandle
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public bool Terminated { get; private set; }

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
        lock (Handles)
        {
            Handles.Add(handle);
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
        await File.WriteAllTextAsync(path, content);
    }
}
