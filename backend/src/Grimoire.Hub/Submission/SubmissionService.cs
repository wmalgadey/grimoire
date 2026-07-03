using Grimoire.Hub.AgentDispatch;
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

    public async Task<string> SubmitAsync(SubmitSourceOptions options, string repoRoot, CancellationToken cancellationToken = default)
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
            WikiDir: System.IO.Path.Combine(repoRoot, "wiki"),
            TasksDir: System.IO.Path.Combine(repoRoot, "tasks"),
            IndexPath: System.IO.Path.Combine(repoRoot, "index.md"),
            LogPath: System.IO.Path.Combine(repoRoot, "log.md"),
            PastedText: options.PastedText);

        var exitCode = await _dispatcher.DispatchAsync(request, cancellationToken);
        var finalStatus = exitCode == 0 ? "completed" : "failed";

        await _repository.UpsertAsync(
            new OperationalTaskState(taskId, finalStatus, null, DateTimeOffset.UtcNow),
            cancellationToken);

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
