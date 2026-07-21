namespace Grimoire.Hub.IngestSubmission;

public sealed record UrlFetchResult(bool Success, byte[]? Content, string? ContentType, int? HttpStatus, string? FailureReason);

/// <summary>
/// Seam between the ingest-submission pipeline and outbound URL fetching (ADR-010 P3).
/// The Hub owns this fetch at submission time; the persisted result is what the
/// triggered Ingest agent run consumes — no re-fetch happens later (FR-010).
/// </summary>
public interface IUrlContentFetcher
{
    Task<UrlFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}
