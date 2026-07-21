using Grimoire.Hub.IngestSubmission;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// Hermetic stand-in for <see cref="IUrlContentFetcher"/>: no network call, a scriptable
/// success/failure result returned for every call.
/// </summary>
public sealed class FakeUrlContentFetcher : IUrlContentFetcher
{
    private readonly UrlFetchResult _result;

    public List<Uri> FetchedUrls { get; } = [];

    public FakeUrlContentFetcher(UrlFetchResult result)
    {
        _result = result;
    }

    public static FakeUrlContentFetcher Succeeding(byte[] content, string contentType = "text/html", int httpStatus = 200) =>
        new(new UrlFetchResult(true, content, contentType, httpStatus, null));

    public static FakeUrlContentFetcher Failing(string failureReason, int? httpStatus = null) =>
        new(new UrlFetchResult(false, null, null, httpStatus, failureReason));

    public Task<UrlFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        lock (FetchedUrls)
        {
            FetchedUrls.Add(url);
        }

        return Task.FromResult(_result);
    }
}
