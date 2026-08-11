using System.Text;
using System.Text.RegularExpressions;
using Grimoire.Hub.IngestSubmission;

namespace Grimoire.Hub.IngestSubmission.Adapters.HttpFetch;

/// <summary>
/// Fetches URL content at ingest-submission time (research.md Decision 3, FR-004; ADR-010
/// P3). The Hub owns this fetch; the persisted result is what the triggered Ingest agent
/// run consumes — no re-fetch happens later (FR-010).
/// </summary>
public sealed class UrlContentFetcher : IUrlContentFetcher
{
    // Sites fronted by bot-protection (confirmed: Atlassian/CloudFront) reject requests that
    // carry no User-Agent header with a 403, even though the same request succeeds from any
    // ordinary browser. Identify honestly as an automated fetcher rather than spoofing a
    // browser UA outright — a descriptive UA is enough to clear UA-less-request blocking.
    private const string RequestUserAgent = "Grimoire-Hub/1.0 (+ingest-fetcher)";
    private const string RequestAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    // A 2xx HTTP status does not guarantee the requested article was actually returned: sites that
    // gate content behind a login (e.g. LinkedIn posts) commonly respond 200 OK with a login/auth
    // wall page instead of a 401/403. Detected either by a redirect landing on a known auth path, or
    // by the returned HTML itself carrying a recognizable auth-wall marker (spec.md Edge Cases:
    // "a URL ... requires authentication the system does not have").
    private static readonly string[] AuthWallPathMarkers =
    [
        "/login", "/uas/login", "/signin", "/sign-in", "/authwall", "/checkpoint/", "/signup"
    ];

    private static readonly string[] AuthWallContentMarkers =
    [
        "authwall",
        "sign in to continue",
        "log in to continue",
        "please log in",
        "please sign in",
        "you must be signed in",
        "join now to see",
    ];

    // Public pages commonly carry auth-wall-sounding phrases in element attributes aimed at
    // logged-out visitors (e.g. GitHub's public repo pages render
    // aria-label="You must be signed in to star a repository" on the Star/Watch buttons shown
    // to everyone, signed in or not). That boilerplate must not trip the content-marker scan
    // below, so marker matching only considers text outside of tag attribute values — stripping
    // quoted attribute values (not full HTML parsing) is enough to tell "marker as visible page
    // text" (a genuine auth wall) apart from "marker as attribute string" (UI chrome).
    private static readonly Regex HtmlTagPattern = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex AttributeValuePattern = new("=\"[^\"]*\"|='[^']*'", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public UrlContentFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(RequestUserAgent);
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd(RequestAccept);
        }
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

            var finalUri = response.RequestMessage?.RequestUri ?? url;
            if (IsAuthWallPath(finalUri))
            {
                return new UrlFetchResult(false, null, null, status,
                    $"URL redirected to an authentication/login page ({finalUri}); the source requires authentication the system does not have");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (bytes.Length == 0)
            {
                return new UrlFetchResult(false, null, contentType, status, "URL returned empty content");
            }

            if (IsHtml(contentType) && ContainsAuthWallMarker(bytes))
            {
                return new UrlFetchResult(false, null, contentType, status,
                    "URL content is an authentication/login wall rather than the requested page; the source requires authentication the system does not have");
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

    private static bool IsAuthWallPath(Uri uri)
        => AuthWallPathMarkers.Any(marker => uri.AbsolutePath.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsHtml(string contentType)
        => contentType.Contains("html", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAuthWallMarker(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var visibleText = StripTagAttributeValues(text);
        return AuthWallContentMarkers.Any(marker => visibleText.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Blanks out quoted attribute values (aria-label="...", title="...", class="...", etc.)
    /// inside HTML tags while leaving tag names and visible text content untouched, so a marker
    /// phrase embedded only in an attribute string (UI chrome for logged-out visitors) is not
    /// mistaken for a marker appearing in the page's actual body text (a genuine auth wall).
    /// </summary>
    private static string StripTagAttributeValues(string html)
        => HtmlTagPattern.Replace(html, tagMatch => AttributeValuePattern.Replace(tagMatch.Value, "=\"\""));
}
