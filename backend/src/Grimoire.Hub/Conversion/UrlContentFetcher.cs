namespace Grimoire.Hub.Conversion;

public sealed record UrlFetchResult(bool Success, byte[]? Content, string? ContentType, int? HttpStatus, string? FailureReason);

/// <summary>
/// Fetches URL content at ingest-submission time (research.md Decision 3, FR-004). The Hub owns
/// this fetch; the persisted result is what the triggered Ingest agent run consumes — no re-fetch
/// happens later (FR-010).
/// </summary>
public sealed class UrlContentFetcher
{
    private readonly HttpClient _httpClient;

    public UrlContentFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UrlFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return new UrlFetchResult(false, null, null, status, $"URL fetch failed with HTTP {status}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (bytes.Length == 0)
            {
                return new UrlFetchResult(false, null, contentType, status, "URL returned empty content");
            }

            return new UrlFetchResult(true, bytes, contentType, status, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UrlFetchResult(false, null, null, null, "URL fetch timed out");
        }
        catch (HttpRequestException ex)
        {
            return new UrlFetchResult(false, null, null, null, $"URL fetch failed: {ex.Message}");
        }
    }
}
