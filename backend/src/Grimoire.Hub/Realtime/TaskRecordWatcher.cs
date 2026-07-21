using System.Collections.Concurrent;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.Runtime.Paths;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.Hub.Realtime;

/// <summary>
/// Hosted background service that watches <see cref="ResolvedGrimoirePaths.TasksDir"/>
/// (ADR-009 — resolved once, never re-derived) for task-record changes and publishes a
/// debounced <c>taskRecordChanged</c> event via <see cref="IngestLifecyclePublisher"/>
/// (contracts/task-record-changed-event.md). Observe-only: this watcher never writes a
/// file itself (ADR-002/ADR-003 — each writer owns its own artifact I/O).
/// </summary>
public sealed class TaskRecordWatcher : BackgroundService
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(1);

    private readonly ResolvedGrimoirePaths _paths;
    private readonly IngestLifecyclePublisher _publisher;
    private readonly ILogger<TaskRecordWatcher> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingDebounces = new(StringComparer.Ordinal);

    private FileSystemWatcher? _watcher;
    private string? _watchDir;
    private CancellationToken _stoppingToken;

    public TaskRecordWatcher(ResolvedGrimoirePaths paths, IngestLifecyclePublisher publisher, ILogger<TaskRecordWatcher>? logger = null)
    {
        _paths = paths;
        _publisher = publisher;
        _logger = logger ?? NullLogger<TaskRecordWatcher>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        Directory.CreateDirectory(_paths.TasksDir);

        // Canonicalize (not re-derive — ADR-009) the configured TasksDir: macOS FSEvents
        // watches registered through a symlinked ancestor (e.g. /var → /private/var, which
        // covers the standard temp dirs) can silently drop events, so the OS watch must be
        // registered on the link-resolved directory.
        _watchDir = ResolveSymlinkedDirectory(_paths.TasksDir);
        StartWatching();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            _watcher?.Dispose();
        }
    }

    private void StartWatching()
    {
        var watcher = new FileSystemWatcher(_watchDir ?? _paths.TasksDir, "*.md")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };

        watcher.Changed += (_, e) => OnFileEvent(e.FullPath);
        watcher.Created += (_, e) => OnFileEvent(e.FullPath);
        watcher.Renamed += (_, e) => OnFileEvent(e.FullPath);
        watcher.Error += (_, e) => OnWatcherError(e.GetException());
        watcher.EnableRaisingEvents = true;

        _watcher = watcher;
    }

    /// <summary>
    /// Test-only seam (see AssemblyInfo.cs): the OS-level triggers for
    /// <see cref="FileSystemWatcher.Error"/> (buffer overflow, watched-directory removal)
    /// are not reliably reproducible in a sandboxed filesystem, so hermetic tests invoke
    /// the real failure-handling path directly instead of a synthetic double.
    /// </summary>
    internal void SimulateWatcherErrorForTests(Exception exception) => OnWatcherError(exception);

    /// <summary>
    /// A lost watch handle or IO error (contracts/task-record-changed-event.md) logs a WARN
    /// event and re-arms a fresh <see cref="FileSystemWatcher"/> after a short delay, so
    /// events resume without an operator restarting the Hub.
    /// </summary>
    private void OnWatcherError(Exception exception)
    {
        IngestSubmissionLogEvents.LogTaskRecordWatchFailed(_logger, _paths.TasksDir, exception.Message);

        _watcher?.Dispose();
        _watcher = null;

        _ = RestartAfterDelayAsync();
    }

    private async Task RestartAfterDelayAsync()
    {
        try
        {
            await Task.Delay(RestartDelay, _stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            StartWatching();
        }
        catch (Exception ex)
        {
            IngestSubmissionLogEvents.LogTaskRecordWatchFailed(_logger, _paths.TasksDir, ex.Message);
        }
    }

    private void OnFileEvent(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);

        // Writer temp-name convention (HubTaskArtifactWriter: ".{name}.{guid}.tmp") never
        // produces events (contracts/task-record-changed-event.md).
        if (fileName.StartsWith('.') || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Debounce(Path.GetFileNameWithoutExtension(fileName));
    }

    /// <summary>
    /// Coalesces rapid successive writes to the same task within <see cref="DebounceWindow"/>
    /// into a single publish carrying the latest observation time.
    /// </summary>
    private void Debounce(string taskId)
    {
        var cts = new CancellationTokenSource();
        _pendingDebounces.AddOrUpdate(taskId, cts, (_, previous) =>
        {
            previous.Cancel();
            previous.Dispose();
            return cts;
        });

        _ = DebounceFireAsync(taskId, cts);
    }

    private async Task DebounceFireAsync(string taskId, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(DebounceWindow, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _pendingDebounces.TryRemove(new KeyValuePair<string, CancellationTokenSource>(taskId, cts));
        await _publisher.PublishTaskRecordChangedAsync(taskId, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    /// <summary>
    /// Resolves every symlinked segment of an existing directory path (the BCL's
    /// <see cref="FileSystemInfo.ResolveLinkTarget"/> only resolves the leaf, but the
    /// macOS temp-dir link sits at an ancestor: /var → /private/var).
    /// </summary>
    private static string ResolveSymlinkedDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (new DirectoryInfo(current).LinkTarget is not null
                && new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = target.FullName;
            }
        }

        return current;
    }
}
