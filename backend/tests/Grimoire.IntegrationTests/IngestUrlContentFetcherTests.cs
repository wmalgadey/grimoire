using System.Net;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.IngestSubmission.Adapters.HttpFetch;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T069 (Convergence) - a 2xx response that is actually an authentication/login wall (e.g. a
/// LinkedIn post URL fetched without credentials) must fail with a human-readable reason instead
/// of being silently converted and stored as if it were the requested article (spec.md Edge Cases,
/// FR-009: "a URL ... requires authentication the system does not have").
/// </summary>
public class IngestUrlContentFetcherTests
{
    [Fact]
    public async Task FetchAsync_Succeeds_ForOrdinaryHtmlArticle()
    {
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, "text/html",
            "<html><body><article>Real article content</article></body></html>");
        var fetcher = new UrlContentFetcher(new HttpClient(handler));

        var result = await fetcher.FetchAsync(new Uri("https://example.test/article"));

        Assert.True(result.Success);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task FetchAsync_Fails_WhenRedirectedToLoginPath()
    {
        using var handler = new RedirectHandler(new Uri("https://www.linkedin.com/login"));
        var fetcher = new UrlContentFetcher(new HttpClient(handler));

        var result = await fetcher.FetchAsync(new Uri("https://www.linkedin.com/posts/some-post-id"));

        Assert.False(result.Success);
        Assert.Contains("authentication", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_Fails_WhenContentIsAnAuthWallPage()
    {
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, "text/html",
            "<html><body><div class=\"authwall\">Sign in to continue reading this post</div></body></html>");
        var fetcher = new UrlContentFetcher(new HttpClient(handler));

        var result = await fetcher.FetchAsync(new Uri("https://www.linkedin.com/posts/some-post-id"));

        Assert.False(result.Success);
        Assert.Contains("authentication", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sites fronted by bot-protection (confirmed: Atlassian/CloudFront) return HTTP 403 for
    /// requests carrying no User-Agent header at all. The fetcher must always send one, whether
    /// its HttpClient was constructed directly (as in these tests) or via DI.
    /// </summary>
    [Fact]
    public async Task FetchAsync_SendsNonEmptyUserAgentHeader()
    {
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, "text/html",
            "<html><body><article>Real article content</article></body></html>");
        var fetcher = new UrlContentFetcher(new HttpClient(handler));

        await fetcher.FetchAsync(new Uri("https://example.test/article"));

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.UserAgent.Count > 0);
    }

    /// <summary>
    /// Public GitHub repo pages render aria-label="You must be signed in to star a repository" on
    /// the Star/Watch buttons shown to every visitor, signed in or not — that boilerplate must not
    /// be mistaken for a genuine auth wall just because the marker phrase happens to appear inside
    /// an attribute string rather than the page's visible body text.
    /// </summary>
    [Fact]
    public async Task FetchAsync_Succeeds_WhenAuthWallMarkerOnlyAppearsInsideAnAttributeValue()
    {
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, "text/html",
            "<html><body>" +
            "<button aria-label=\"You must be signed in to star a repository\">Star</button>" +
            "<article>Real public repository content</article>" +
            "</body></html>");
        var fetcher = new UrlContentFetcher(new HttpClient(handler));

        var result = await fetcher.FetchAsync(new Uri("https://github.com/ilindaniel/ponytail-lite"));

        Assert.True(result.Success);
        Assert.Null(result.FailureReason);
    }

    private sealed class StaticResponseHandler(HttpStatusCode status, string contentType, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>Simulates HttpClient's automatic-redirect-following landing on a login page.</summary>
    private sealed class RedirectHandler(Uri finalUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri),
                Content = new StringContent("<html><body>Sign in</body></html>", System.Text.Encoding.UTF8, "text/html"),
            };
            return Task.FromResult(response);
        }
    }
}
