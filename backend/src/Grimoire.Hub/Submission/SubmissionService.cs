using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.Submission;

public sealed class SubmissionService
{
    private readonly OperationalStateRepository _repository;
    private readonly IAgentProcessLauncher _processHost;
    private readonly ILogger<SubmissionService> _logger;

    public SubmissionService(OperationalStateRepository repository, IAgentProcessLauncher processHost, ILogger<SubmissionService>? logger = null)
    {
        _repository = repository;
        _processHost = processHost;
        _logger = logger ?? NullLogger<SubmissionService>.Instance;
    }

    public async Task<string> SubmitAsync(SubmitSourceOptions options, ContentRootPaths contentPaths, CancellationToken cancellationToken = default)
    {
        var taskId = $"{DateTime.UtcNow:yyyy-MM-dd}-ingest-{Guid.NewGuid():N}";
        var normalizedSourceRef = ResolveSourcePath(options.Path);

        using var submitSpan = HubTracing.ActivitySource.StartActivity("hub.ingest.submit");
        submitSpan?.SetTag("task_id", taskId);
        submitSpan?.SetTag("source_ref", normalizedSourceRef);

        using var taskCreatedSpan = HubTracing.ActivitySource.StartActivity("ingest.task.created");
        taskCreatedSpan?.SetTag("signal_type", "log");
        taskCreatedSpan?.SetTag("event_name", "ingest.task.created");
        taskCreatedSpan?.SetTag("level", "Information");
        taskCreatedSpan?.SetTag("task_id", taskId);
        taskCreatedSpan?.SetTag("source_ref", normalizedSourceRef);

        _logger.LogInformation(new EventId(1, "ingest.task.created"),
            "Ingest task created: {task_id}, source: {source_ref}", taskId, normalizedSourceRef);

        await _repository.UpsertAsync(
            new OperationalTaskState(taskId, "running", null, DateTimeOffset.UtcNow),
            cancellationToken);

        var request = new IngestAgentRequest(
            TaskId: taskId,
            SourceRef: normalizedSourceRef,
            SourceKind: options.SourceKind,
            WikiRoot: contentPaths.Root,
            PagesDir: contentPaths.PagesDir,
            TasksDir: contentPaths.TasksDir,
            IndexPath: contentPaths.IndexPath,
            LogPath: contentPaths.LogPath,
            PastedText: options.PastedText,
            SystemPromptPath: contentPaths.SystemPromptPath,
            DefaultUserPromptPath: contentPaths.DefaultUserPromptPath,
            PolicyPath: contentPaths.PolicyPath);

        using (var spawnSpan = HubTracing.ActivitySource.StartActivity("hub.ingest.spawn_agent"))
        {
            spawnSpan?.SetTag("task_id", taskId);
            spawnSpan?.SetTag("agent", "ingest");
            // Manual CLI path: run-to-exit is explicitly permitted here (ADR-008 exit-code
            // note); the web dispatch path goes through IngestRunCoordinator instead.
            await _processHost.RunToExitAsync(request, cancellationToken);
        }

        // Task has reached a terminal state; delete the row so the DB doesn't grow unboundedly.
        // History is captured in the task artifact and log.md (ADR-003).
        await _repository.DeleteAsync(taskId, cancellationToken);

        return taskId;
    }

    private static string ResolveSourcePath(string sourcePath)
    {
        if (Path.IsPathRooted(sourcePath))
        {
            return sourcePath;
        }

        // FR-003 / spec edge case: relative source paths resolve against the process
        // working directory, never a discovered repository root (ADR-009 single
        // composition point owns the one sanctioned ambient-directory read).
        var cwd = GrimoirePathResolver.CurrentWorkingDirectory;
        var candidates = new List<string>
        {
            Path.GetFullPath(sourcePath, cwd),
        };

        var current = new DirectoryInfo(cwd);
        while (current is not null)
        {
            candidates.Add(Path.GetFullPath(Path.Combine(current.FullName, sourcePath)));
            current = current.Parent;
        }

        var existing = candidates.FirstOrDefault(File.Exists);
        return existing ?? candidates[0];
    }
}
