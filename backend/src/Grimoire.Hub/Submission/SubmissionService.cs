using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.ContentRoot;
using Grimoire.Hub.OperationalState;

namespace Grimoire.Hub.Submission;

public sealed class SubmissionService
{
    private readonly OperationalStateRepository _repository;
    private readonly IngestAgentDispatcher _dispatcher;

    public SubmissionService(OperationalStateRepository repository, IngestAgentDispatcher dispatcher)
    {
        _repository = repository;
        _dispatcher = dispatcher;
    }

    public async Task<string> SubmitAsync(SubmitSourceOptions options, string repoRoot, ContentRootPaths contentPaths, CancellationToken cancellationToken = default)
    {
        var taskId = $"{DateTime.UtcNow:yyyy-MM-dd}-ingest-{Guid.NewGuid():N}";
        var normalizedSourceRef = ResolveSourcePath(options.Path, repoRoot);

        await _repository.UpsertAsync(
            new OperationalTaskState(taskId, "running", null, DateTimeOffset.UtcNow),
            cancellationToken);

        var request = new IngestAgentRequest(
            TaskId: taskId,
            SourceRef: normalizedSourceRef,
            SourceKind: options.SourceKind,
            PagesDir: contentPaths.PagesDir,
            TasksDir: contentPaths.TasksDir,
            IndexPath: contentPaths.IndexPath,
            LogPath: contentPaths.LogPath,
            PastedText: options.PastedText);

        var exitCode = await _dispatcher.DispatchAsync(request, cancellationToken);

        // Task has reached a terminal state; delete the row so the DB doesn't grow unboundedly.
        // History is captured in the task artifact and log.md (ADR-003).
        await _repository.DeleteAsync(taskId, cancellationToken);

        return taskId;
    }

    private static string ResolveSourcePath(string sourcePath, string repoRoot)
    {
        if (Path.IsPathRooted(sourcePath))
        {
            return sourcePath;
        }

        var cwd = Directory.GetCurrentDirectory();
        var candidates = new List<string>
        {
            Path.GetFullPath(sourcePath, cwd),
            Path.GetFullPath(sourcePath, repoRoot),
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
