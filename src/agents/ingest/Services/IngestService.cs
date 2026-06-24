using System.Diagnostics;
using Grimoire.Ingest.Cache;
using Grimoire.Ingest.Git;
using Grimoire.Ingest.Hub;
using Grimoire.Ingest.Models;
using Grimoire.Ingest.Pipeline;

namespace Grimoire.Ingest.Services;

public class IngestService
{
    private IngestRun? _activeRun;
    private readonly object _runLock = new();

    private readonly IngestPipeline _pipeline;
    private readonly IngestCache _cache;
    private readonly IngestGitService _gitService;
    private readonly HubReporter _hubReporter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestService> _logger;
    private readonly IngestMetrics _metrics;

    public IngestService(
        IngestPipeline pipeline,
        IngestCache cache,
        IngestGitService gitService,
        HubReporter hubReporter,
        IConfiguration configuration,
        ILogger<IngestService> logger,
        IngestMetrics metrics)
    {
        _pipeline = pipeline;
        _cache = cache;
        _gitService = gitService;
        _hubReporter = hubReporter;
        _configuration = configuration;
        _logger = logger;
        _metrics = metrics;
    }

    public bool IsRunActive
    {
        get { lock (_runLock) return _activeRun != null; }
    }

    public IngestRun? GetActiveRun()
    {
        lock (_runLock) return _activeRun;
    }

    public async Task<IngestRun> TriggerRunAsync(string runId)
    {
        IngestRun run;
        lock (_runLock)
        {
            if (_activeRun != null)
                return _activeRun;

            run = new IngestRun { RunId = runId };
            _activeRun = run;
        }

        _metrics.SetActiveRun(true);

        _ = Task.Run(() => ExecuteRunAsync(run));
        return run;
    }

    private async Task ExecuteRunAsync(IngestRun run)
    {
        using var activity = IngestTracing.Source.StartActivity("ingest.run");
        activity?.SetTag("run_id", run.RunId);

        var sourceDir = _configuration["IngestSourceDir"] ?? "raw/sources";
        var sw = Stopwatch.StartNew();

        var files = Directory.Exists(sourceDir)
            ? Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).ToList()
            : new List<string>();

        run.TotalFiles = files.Count;
        activity?.SetTag("file_count", files.Count);

        _logger.LogInformation(
            "ingest.run_started run_id={RunId} source_dir={SourceDir} file_count={FileCount}",
            run.RunId, sourceDir, files.Count);

        var wikiFiles = new List<string>();

        foreach (var filePath in files)
        {
            var sha256 = IngestCache.ComputeSha256(filePath);
            var alreadyProcessed = await _cache.IsProcessedAsync(filePath, sha256);

            if (alreadyProcessed)
            {
                _logger.LogInformation(
                    "ingest.file_skipped file_path={FilePath} reason=cache_hit",
                    filePath);
                run.SkippedCount++;
                _metrics.RecordFile("skipped");
                continue;
            }

            try
            {
                var result = await _pipeline.ProcessFileAsync(filePath, run.RunId, run);

                if (result.Status == "Processed")
                {
                    await _cache.MarkProcessedAsync(filePath, sha256, result.ChunkCount);
                    run.ProcessedCount++;
                    run.TotalChunks += result.ChunkCount;

                    // Collect wiki output path for git commit
                    var outputPath = GetWikiOutputPath(filePath, sourceDir);
                    if (File.Exists(outputPath))
                        wikiFiles.Add(outputPath);
                }
                else if (result.Status == "Skipped")
                {
                    await _cache.MarkSkippedAsync(filePath, sha256, result.ErrorMessage);
                    run.SkippedCount++;
                    _metrics.RecordFile("skipped");
                }
                else
                {
                    await _cache.MarkFailedAsync(filePath, sha256, result.ErrorMessage ?? "Unknown error");
                    run.FailedCount++;
                    _metrics.RecordFile("failed");
                }

                lock (run.FileResults)
                    run.FileResults.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ingest.file_failed file_path={FilePath} error={Error} stage=pipeline",
                    filePath, ex.Message);

                await _cache.MarkFailedAsync(filePath, sha256, ex.Message);
                run.FailedCount++;
                _metrics.RecordFile("failed");

                lock (run.FileResults)
                    run.FileResults.Add(new IngestFileResult
                    {
                        FilePath = filePath,
                        Status = "Failed",
                        ErrorMessage = ex.Message
                    });
            }
        }

        // Git commit for all wiki files produced in this run
        if (wikiFiles.Count > 0)
        {
            using var gitActivity = IngestTracing.Source.StartActivity("ingest.git.commit");
            gitActivity?.SetTag("file_count", wikiFiles.Count);

            var commitSha = await _gitService.CommitAsync(wikiFiles, run.ProcessedCount, run.TotalChunks);

            if (!string.IsNullOrEmpty(commitSha))
            {
                _metrics.RecordGitCommit();
                gitActivity?.SetTag("commit_sha", commitSha);
            }
        }

        sw.Stop();
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.Status = run.FailedCount > 0 && run.ProcessedCount == 0
            ? RunStatus.Failed
            : RunStatus.Completed;

        _logger.LogInformation(
            "ingest.run_completed run_id={RunId} processed={Processed} failed={Failed} skipped={Skipped} duration_ms={DurationMs}",
            run.RunId, run.ProcessedCount, run.FailedCount, run.SkippedCount, sw.ElapsedMilliseconds);

        await _hubReporter.PostRunCompletedAsync(new RunCompletedPayload(
            RunId: run.RunId,
            Status: run.Status.ToString(),
            CompletedAt: run.CompletedAt.Value,
            Summary: new
            {
                totalFiles = run.TotalFiles,
                processedCount = run.ProcessedCount,
                failedCount = run.FailedCount,
                skippedCount = run.SkippedCount,
                totalChunks = run.TotalChunks,
                durationMs = (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue),
                files = run.FileResults
            }));

        lock (_runLock)
            _activeRun = null;

        _metrics.SetActiveRun(false);
    }

    public async Task ProcessFileAsync(string filePath)
    {
        // Called by SourceWatcher for individual file events
        IngestRun run;
        lock (_runLock)
        {
            if (_activeRun != null)
            {
                run = _activeRun;
            }
            else
            {
                run = new IngestRun { RunId = Guid.NewGuid().ToString() };
                _activeRun = run;
            }
        }

        _metrics.SetActiveRun(true);

        var sha256 = IngestCache.ComputeSha256(filePath);
        var alreadyProcessed = await _cache.IsProcessedAsync(filePath, sha256);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "ingest.file_skipped file_path={FilePath} reason=cache_hit",
                filePath);
            return;
        }

        try
        {
            var sourceDir = _configuration["IngestSourceDir"] ?? "raw/sources";
            run.TotalFiles++;

            var result = await _pipeline.ProcessFileAsync(filePath, run.RunId, run);

            if (result.Status == "Processed")
            {
                await _cache.MarkProcessedAsync(filePath, sha256, result.ChunkCount);
                run.ProcessedCount++;
                run.TotalChunks += result.ChunkCount;

                var outputPath = GetWikiOutputPath(filePath, sourceDir);
                if (File.Exists(outputPath))
                    await _gitService.CommitAsync([outputPath], 1, result.ChunkCount);

                _metrics.RecordGitCommit();
            }
            else if (result.Status == "Skipped")
            {
                await _cache.MarkSkippedAsync(filePath, sha256, result.ErrorMessage);
                run.SkippedCount++;
            }
            else
            {
                await _cache.MarkFailedAsync(filePath, sha256, result.ErrorMessage ?? "Unknown error");
                run.FailedCount++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ingest.file_failed file_path={FilePath} error={Error} stage=watcher-pipeline",
                filePath, ex.Message);
            await _cache.MarkFailedAsync(filePath, sha256, ex.Message);
        }
        finally
        {
            lock (_runLock)
            {
                if (_activeRun?.RunId == run.RunId)
                    _activeRun = null;
            }
            _metrics.SetActiveRun(false);
        }
    }

    private static string GetWikiOutputPath(string filePath, string sourceDir)
    {
        var relativePath = Path.GetRelativePath(sourceDir, filePath);
        var outputRelative = Path.ChangeExtension(relativePath, ".md");
        return Path.Combine("wiki", outputRelative);
    }
}
