using Grimoire.Hub.AgentDispatch;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Test double for <see cref="IIngestAgentDispatcher"/> (Test Strategy, SC-006): simulates the
/// Ingest agent's own task-artifact writes ("running" then a terminal status) without spawning a
/// real child process or needing live model credentials.
/// </summary>
public sealed class FakeIngestAgentDispatcher : IIngestAgentDispatcher
{
    private readonly string _terminalStatus;
    private readonly string? _failureReason;
    private readonly TimeSpan _simulatedRunDuration;

    public List<IngestAgentRequest> Requests { get; } = [];
    public List<(DateTimeOffset Started, DateTimeOffset Finished)> RunWindows { get; } = [];

    public FakeIngestAgentDispatcher(string terminalStatus = "completed", string? failureReason = null, TimeSpan? simulatedRunDuration = null)
    {
        _terminalStatus = terminalStatus;
        _failureReason = failureReason;
        _simulatedRunDuration = simulatedRunDuration ?? TimeSpan.Zero;
    }

    public async Task<int> DispatchAsync(IngestAgentRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var started = DateTimeOffset.UtcNow;

        var taskArtifactPath = Path.Combine(request.TasksDir, $"{request.TaskId}.md");
        await WriteArtifactAsync(taskArtifactPath, request, "running", null);

        if (_simulatedRunDuration > TimeSpan.Zero)
        {
            await Task.Delay(_simulatedRunDuration, cancellationToken);
        }

        await WriteArtifactAsync(taskArtifactPath, request, _terminalStatus, _failureReason);
        RunWindows.Add((started, DateTimeOffset.UtcNow));

        return _terminalStatus == "failed" ? 1 : 0;
    }

    private static async Task WriteArtifactAsync(string path, IngestAgentRequest request, string status, string? failureReason)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var failure = failureReason is null ? "null" : $"\"{failureReason}\"";
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
            failure_reason: {failure}
            ---

            Fake agent run ({status}).
            """;
        await File.WriteAllTextAsync(path, content);
    }
}
