using System.Security.Cryptography;
using Grimoire.Ingest.Models;

namespace Grimoire.Ingest.Cache;

public class IngestCache
{
    private readonly IngestCacheRepository _repository;
    private readonly ILogger<IngestCache> _logger;

    public IngestCache(IngestCacheRepository repository, ILogger<IngestCache> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public async Task<bool> IsProcessedAsync(string filePath, string sha256)
    {
        var record = await _repository.GetRecordAsync(filePath);
        if (record == null)
            return false;

        return record.Sha256 == sha256 && record.Status == IngestStatus.Processed;
    }

    public async Task MarkProcessedAsync(string filePath, string sha256, int chunkCount)
    {
        var record = new IngestRecord
        {
            FilePath = filePath,
            Sha256 = sha256,
            Status = IngestStatus.Processed,
            ProcessedAt = DateTimeOffset.UtcNow,
            ChunkCount = chunkCount
        };
        await _repository.SaveRecordAsync(record);
        _logger.LogInformation("ingest.file_processed file_path={FilePath} chunk_count={ChunkCount}", filePath, chunkCount);
    }

    public async Task MarkFailedAsync(string filePath, string sha256, string error)
    {
        var record = new IngestRecord
        {
            FilePath = filePath,
            Sha256 = sha256,
            Status = IngestStatus.Failed,
            ProcessedAt = DateTimeOffset.UtcNow,
            ErrorMessage = error
        };
        await _repository.SaveRecordAsync(record);
        _logger.LogError("ingest.file_failed file_path={FilePath} error={Error}", filePath, error);
    }

    public async Task MarkSkippedAsync(string filePath, string sha256, string? reason)
    {
        var record = new IngestRecord
        {
            FilePath = filePath,
            Sha256 = sha256,
            Status = IngestStatus.Skipped,
            ProcessedAt = DateTimeOffset.UtcNow,
            ErrorMessage = reason
        };
        await _repository.SaveRecordAsync(record);
        _logger.LogInformation("ingest.file_skipped file_path={FilePath} reason={Reason}", filePath, reason ?? "cache_hit");
    }

    public async Task<(string? action, string? tag)> GetFeedbackDecisionAsync(string filePath)
    {
        var record = await _repository.GetRecordAsync(filePath);
        if (record == null)
            return (null, null);

        return (record.FeedbackAction, record.FeedbackTag);
    }
}
