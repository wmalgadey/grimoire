using System.Threading.Channels;

namespace Grimoire.Ingest.Watcher;

public class SourceWatcher : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SourceWatcher> _logger;
    private readonly IServiceProvider _serviceProvider;
    private FileSystemWatcher? _watcher;
    private readonly Channel<string> _fileQueue;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;

    // Debounce: per-file CTS to cancel pending trigger
    private readonly Dictionary<string, CancellationTokenSource> _debouncers = new();
    private readonly SemaphoreSlim _debounceLock = new(1, 1);

    public SourceWatcher(
        IConfiguration configuration,
        ILogger<SourceWatcher> logger,
        IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _fileQueue = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var sourceDir = _configuration["IngestSourceDir"] ?? "raw/sources";

        if (!Directory.Exists(sourceDir))
        {
            Directory.CreateDirectory(sourceDir);
            _logger.LogInformation("Created source directory: {SourceDir}", sourceDir);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _watcher = new FileSystemWatcher(sourceDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;

        _processingTask = ProcessQueueAsync(_cts.Token);

        _logger.LogInformation("SourceWatcher started on {SourceDir}", sourceDir);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_cts != null)
        {
            await _cts.CancelAsync();
        }

        _fileQueue.Writer.TryComplete();

        if (_processingTask != null)
        {
            try { await _processingTask; }
            catch (OperationCanceledException) { /* expected */ }
        }

        _logger.LogInformation("SourceWatcher stopped");
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.FullPath))
            return;

        _ = DebounceAsync(e.FullPath);
    }

    private async Task DebounceAsync(string filePath)
    {
        CancellationTokenSource newCts;
        await _debounceLock.WaitAsync();
        try
        {
            if (_debouncers.TryGetValue(filePath, out var existing))
            {
                await existing.CancelAsync();
                existing.Dispose();
            }

            newCts = new CancellationTokenSource();
            _debouncers[filePath] = newCts;
        }
        finally
        {
            _debounceLock.Release();
        }

        try
        {
            await Task.Delay(300, newCts.Token);

            await _debounceLock.WaitAsync();
            try { _debouncers.Remove(filePath); }
            finally { _debounceLock.Release(); }

            await _fileQueue.Writer.WriteAsync(filePath);
        }
        catch (OperationCanceledException)
        {
            // Debounced — a newer event will handle this file
        }
        finally
        {
            newCts.Dispose();
        }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        await foreach (var filePath in _fileQueue.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var ingestService = scope.ServiceProvider.GetRequiredService<Services.IngestService>();
                await ingestService.ProcessFileAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SourceWatcher: error processing {FilePath}", filePath);
            }
        }
    }
}
