using System.Net;
using Grimoire.Hub.Conversion;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T069 (Convergence) - a 2xx response that is actually an authentication/login wall (e.g. a
/// LinkedIn post URL fetched without credentials) must fail with a human-readable reason instead
/// of being silently converted and stored as if it were the requested article (spec.md Edge Cases,
/// FR-009: "a URL ... requires authentication the system does not have").
/// </summary>
public class UrlContentFetcherTests
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

    private sealed class StaticResponseHandler(HttpStatusCode status, string contentType, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
