using System.Collections.Concurrent;
using System.Diagnostics;
using Grimoire.Ingest.Cache;
using Grimoire.Ingest.Hub;
using Grimoire.Ingest.Models;
using Grimoire.Ingest.Services;

namespace Grimoire.Ingest.Pipeline;

public class IngestPipeline
{
    private readonly Chunker _chunker;
    private readonly LlmAnalyzer _llmAnalyzer;
    private readonly Indexer _indexer;
    private readonly IngestCache _cache;
    private readonly IngestCacheRepository _repository;
    private readonly HubReporter _hubReporter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestPipeline> _logger;
    private readonly IngestMetrics _metrics;

    // Pending feedback: keyed by RequestId
    private readonly ConcurrentDictionary<string, TaskCompletionSource<FeedbackResponse>> _pendingFeedback = new();

    private static readonly HashSet<string> KnownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".pdf", ".json", ".yaml", ".yml", ".csv", ".rst", ".html", ".htm"
    };

    public IngestPipeline(
        Chunker chunker,
        LlmAnalyzer llmAnalyzer,
        Indexer indexer,
        IngestCache cache,
        IngestCacheRepository repository,
        HubReporter hubReporter,
        IConfiguration configuration,
        ILogger<IngestPipeline> logger,
        IngestMetrics metrics)
    {
        _chunker = chunker;
        _llmAnalyzer = llmAnalyzer;
        _indexer = indexer;
        _cache = cache;
        _repository = repository;
        _hubReporter = hubReporter;
        _configuration = configuration;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<IngestFileResult> ProcessFileAsync(
        string filePath,
        string runId,
        IngestRun run)
    {
        var sw = Stopwatch.StartNew();
        using var activity = IngestTracing.Source.StartActivity("ingest.file.process");
        activity?.SetTag("file_path", filePath);

        var sha256 = IngestCache.ComputeSha256(filePath);
        activity?.SetTag("sha256", sha256);

        _logger.LogInformation(
            "ingest.file_detected file_path={FilePath} sha256={Sha256}",
            filePath, sha256);

        // Check for ambiguity
        var ambiguityReason = DetectAmbiguity(filePath);
        if (ambiguityReason.HasValue)
        {
            // Check if feedback was already given for this file
            var (cachedAction, cachedTag) = await _cache.GetFeedbackDecisionAsync(filePath);
            if (cachedAction != null)
            {
                if (cachedAction == "Skip")
                {
                    await _cache.MarkSkippedAsync(filePath, sha256, "feedback:skip");
                    sw.Stop();
                    return new IngestFileResult
                    {
                        FilePath = filePath,
                        Status = "Skipped",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }
                // Process or Tag: continue with tag as hint
            }
            else
            {
                var feedbackResponse = await RequestFeedbackAsync(filePath, runId, ambiguityReason.Value);

                _logger.LogInformation(
                    "ingest.feedback_received file_path={FilePath} action={Action}",
                    filePath, feedbackResponse.Action);

                _metrics.RecordFeedbackRequest(ambiguityReason.Value.ToString());

                if (feedbackResponse.Action == FeedbackAction.Skip)
                {
                    await _cache.MarkSkippedAsync(filePath, sha256, "feedback:skip");
                    sw.Stop();
                    return new IngestFileResult
                    {
                        FilePath = filePath,
                        Status = "Skipped",
                        DurationMs = sw.ElapsedMilliseconds
                    };
                }
            }
        }

        // Read file content
        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ingest.file_failed file_path={FilePath} error={Error} stage=read",
                filePath, ex.Message);
            sw.Stop();
            return new IngestFileResult
            {
                FilePath = filePath,
                Status = "Failed",
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        // Chunk
        List<Chunk> chunks;
        using (IngestTracing.Source.StartActivity("ingest.pipeline.chunk"))
        {
            chunks = _chunker.Chunk(content, filePath);
        }

        _metrics.RecordChunks(chunks.Count);

        // Analyze each chunk
        var analyses = new List<ChunkAnalysis>();
        foreach (var chunk in chunks)
        {
            var analysis = await _llmAnalyzer.AnalyzeChunkAsync(chunk, filePath);
            analyses.Add(analysis);
        }

        // Index
        var sourceDir = _configuration["IngestSourceDir"] ?? "raw/sources";
        string outputPath;
        try
        {
            outputPath = await _indexer.WriteIndexAsync(filePath, sourceDir, analyses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ingest.file_failed file_path={FilePath} error={Error} stage=index",
                filePath, ex.Message);
            sw.Stop();
            return new IngestFileResult
            {
                FilePath = filePath,
                Status = "Failed",
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };
        }

        sw.Stop();

        // Post progress to Hub
        await _hubReporter.PostProgressAsync(new IngestProgressPayload(
            RunId: runId,
            FilePath: filePath,
            Status: "Processed",
            ChunkCount: chunks.Count,
            DurationMs: sw.ElapsedMilliseconds,
            ProcessedCount: run.ProcessedCount + 1,
            FailedCount: run.FailedCount,
            SkippedCount: run.SkippedCount,
            TotalFiles: run.TotalFiles));

        _metrics.RecordFile("processed");

        return new IngestFileResult
        {
            FilePath = filePath,
            Status = "Processed",
            ChunkCount = chunks.Count,
            DurationMs = sw.ElapsedMilliseconds
        };
    }

    public FeedbackReason? DetectAmbiguity(string filePath)
    {
        var ext = Path.GetExtension(filePath);

        if (string.IsNullOrEmpty(ext))
            return FeedbackReason.MissingMetadata;

        if (!KnownExtensions.Contains(ext))
            return FeedbackReason.UnknownFormat;

        var fileSizeLimitMb = _configuration.GetValue<int>("IngestFileSizeLimitMb", 10);
        var info = new FileInfo(filePath);
        if (info.Exists && info.Length > fileSizeLimitMb * 1024L * 1024L)
            return FeedbackReason.Oversized;

        return null;
    }

    private async Task<FeedbackResponse> RequestFeedbackAsync(
        string filePath,
        string runId,
        FeedbackReason reason)
    {
        var request = new FeedbackRequest
        {
            RunId = runId,
            FilePath = filePath,
            Reason = reason
        };

        await _repository.SaveFeedbackRequestAsync(request);

        _logger.LogInformation(
            "ingest.feedback_requested file_path={FilePath} reason={Reason}",
            filePath, reason);

        await _hubReporter.PostFeedbackRequestAsync(new IngestFeedbackPayload(
            RequestId: request.RequestId,
            RunId: runId,
            FilePath: filePath,
            Reason: reason.ToString(),
            RaisedAt: request.RaisedAt));

        var tcs = new TaskCompletionSource<FeedbackResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingFeedback[request.RequestId] = tcs;

        return await tcs.Task;
    }

    public void ResolveFeedback(string requestId, FeedbackResponse response)
    {
        if (_pendingFeedback.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }
}
