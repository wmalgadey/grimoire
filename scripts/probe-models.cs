#!/usr/bin/env dotnet
#:package Anthropic@12.40.0
#:property Nullable=enable
// The Anthropic SDK serializes its param models with reflection-based System.Text.Json.
// .NET 10 file-based apps disable that by default, which throws inside TextBlockParam's
// constructor before a single request is sent.
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true

// Probes which models the configured Anthropic credential is actually entitled to use.
//
// Why this exists: an OAuth-style credential (sk-ant-oat...) answers a request for a model
// it is not entitled to with "429 rate_limit_error" — indistinguishable, from the status
// alone, from a genuine rate limit. deploy/README.md documents the discriminator: a real
// rate limit carries anthropic-ratelimit-unified-* response headers, an entitlement gap
// carries none. This script sends one 1-token message per model and classifies on that.
//
// Usage, from the repository root:
//   dotnet run scripts/probe-models.cs                    # catalog + built-in candidates
//   dotnet run scripts/probe-models.cs claude-opus-5 ...  # only these model ids
//
// Without a local .NET 10 SDK, in a container. Source .env into the shell and forward the
// variables by name rather than using --env-file: docker does not strip the quotes around
// the model values in .env, and this keeps the token off the command line.
//
//   set -a; . .env; set +a
//   docker run --rm --user "$(id -u):$(id -g)" -e HOME=/work \
//     -e ANTHROPIC_AUTH_TOKEN -e GRIMOIRE_INGEST_MODEL -e GRIMOIRE_QUERY_MODEL -e GRIMOIRE_LINT_MODEL \
//     -v "$PWD":/work -w /work mcr.microsoft.com/dotnet/sdk:10.0 \
//     dotnet run scripts/probe-models.cs
//
// Reads: ANTHROPIC_AUTH_TOKEN (preferred) or ANTHROPIC_API_KEY,
//        ANTHROPIC_BASE_URL / GRIMOIRE_INGEST_BASE_URL,
//        GRIMOIRE_{INGEST,QUERY,LINT}_MODEL (added to the probe set so you see whether the
//        models your deployment is actually configured for are covered).
//        GRIMOIRE_PROBE_OAUTH_BETA=0 disables the anthropic-beta: oauth-2025-04-20 header.
//
// Exit: 0 = at least one model available, 1 = none available, 2 = misconfigured.
//
// NOTE: every successful probe consumes a few tokens from the 5-hour / 7-day window.

using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using System.Text.Json;

const string OAuthBetaHeader = "oauth-2025-04-20";

var authToken = Env("ANTHROPIC_AUTH_TOKEN");
var apiKey = Env("ANTHROPIC_API_KEY");
var baseUrl = Env("ANTHROPIC_BASE_URL") ?? Env("GRIMOIRE_INGEST_BASE_URL") ?? "https://api.anthropic.com";

if (authToken is null && apiKey is null)
{
    Console.Error.WriteLine("No credential: set ANTHROPIC_AUTH_TOKEN (sk-ant-oat...) or ANTHROPIC_API_KEY (sk-ant-api...).");
    return 2;
}

// An oat token authenticates as Authorization: Bearer and is rejected with a bare 401 under
// x-api-key, so the two credential kinds must not be mixed on one client.
var useOAuth = authToken is not null;
var secret = authToken ?? apiKey!;
var sendBeta = useOAuth && Env("GRIMOIRE_PROBE_OAUTH_BETA") != "0";

Console.WriteLine($"credential : {Mask(secret)}  ({(useOAuth ? "ANTHROPIC_AUTH_TOKEN -> Authorization: Bearer" : "ANTHROPIC_API_KEY -> x-api-key")})");
Console.WriteLine($"base url   : {baseUrl}");
Console.WriteLine($"beta header: {(sendBeta ? $"anthropic-beta: {OAuthBetaHeader}" : "(none)")}");
Console.WriteLine();

var probe = new ProbeHandler(sendBeta);

// MaxRetries = 0 is essential: the SDK retries 429 twice by default with backoff, which for
// an entitlement gap is pure latency, and for a genuine rate limit burns the window further.
var client = useOAuth
    ? new AnthropicClient
    {
        BaseUrl = baseUrl,
        AuthToken = secret,
        Timeout = TimeSpan.FromSeconds(30),
        MaxRetries = 0,
        Handlers = [probe],
    }
    : new AnthropicClient
    {
        BaseUrl = baseUrl,
        ApiKey = secret,
        Timeout = TimeSpan.FromSeconds(30),
        MaxRetries = 0,
        Handlers = [probe],
    };

var models = args.Length > 0
    ? args.Distinct(StringComparer.Ordinal).ToList()
    : await BuildProbeSetAsync(baseUrl, secret, useOAuth, sendBeta);

Console.WriteLine($"{"MODEL",-34} {"VERDICT",-22} DETAIL");
Console.WriteLine(new string('-', 100));

var results = new List<(string Model, Verdict Verdict)>();

foreach (var model in models)
{
    probe.Reset();
    var verdict = await ProbeModelAsync(client, model, probe);
    results.Add((model, verdict));
    Console.WriteLine($"{model,-34} {verdict.Label,-22} {verdict.Detail}");
    await Task.Delay(250);
}

Console.WriteLine();
var available = results.Where(r => r.Verdict.Kind == VerdictKind.Available).Select(r => r.Model).ToList();
Console.WriteLine(available.Count > 0
    ? $"available ({available.Count}): {string.Join(", ", available)}"
    : "available: none — this credential cannot run any probed model.");

foreach (var name in new[] { "GRIMOIRE_INGEST_MODEL", "GRIMOIRE_QUERY_MODEL", "GRIMOIRE_LINT_MODEL" })
{
    var configured = Env(name);
    var mark = configured is null
        ? "unset -> falls back to AnthropicModelClient's hardcoded default (claude-opus-4-8)"
        : available.Contains(configured, StringComparer.Ordinal) ? "OK" : "NOT AVAILABLE with this credential";
    Console.WriteLine($"{name,-22} = {configured ?? "(unset)",-28} {mark}");
}

return available.Count > 0 ? 0 : 1;

// ---------------------------------------------------------------------------

async Task<Verdict> ProbeModelAsync(AnthropicClient c, string model, ProbeHandler h)
{
    var blocks = new List<ContentBlockParam> { new(new TextBlockParam("hi"), null) };
    var messages = new List<MessageParam>
    {
        new() { Role = Role.User, Content = new MessageParamContent(blocks, null) },
    };

    var parameters = new MessageCreateParams
    {
        Model = model,
        MaxTokens = 1,
        Messages = messages,
    };

    try
    {
        await c.Messages.Create(parameters);
        var unified = h.UnifiedRateLimitHeaders();
        return new Verdict(VerdictKind.Available, "AVAILABLE",
            unified.Count > 0 ? string.Join("  ", unified.Select(kv => $"{kv.Key.Replace("anthropic-ratelimit-unified-", "")}={kv.Value}")) : "200 OK");
    }
    catch (AnthropicApiException ex)
    {
        var status = (int)ex.StatusCode;
        var (errorType, message) = ParseError(ex.ResponseBody);
        var hasUnified = h.UnifiedRateLimitHeaders().Count > 0;

        // The whole point of the script: 429 + no unified-ratelimit headers = entitlement gap,
        // 429 + headers = a real limit you can wait out.
        return status switch
        {
            429 when !hasUnified => new Verdict(VerdictKind.NotEntitled, "NOT ENTITLED",
                $"429 {errorType} without anthropic-ratelimit-unified-* headers -- {message}"),
            429 => new Verdict(VerdictKind.RateLimited, "RATE LIMITED",
                $"429 {errorType}, real limit: {string.Join("  ", h.UnifiedRateLimitHeaders().Select(kv => $"{kv.Key.Replace("anthropic-ratelimit-unified-", "")}={kv.Value}"))}"),
            401 => new Verdict(VerdictKind.Auth, "AUTH REJECTED", $"401 {errorType} -- {message}"),
            403 => new Verdict(VerdictKind.Auth, "FORBIDDEN", $"403 {errorType} -- {message}"),
            404 => new Verdict(VerdictKind.UnknownModel, "UNKNOWN MODEL", $"404 {errorType} -- {message}"),
            _ => new Verdict(VerdictKind.Other, $"HTTP {status}", $"{errorType} -- {message}"),
        };
    }
    catch (Exception ex)
    {
        return new Verdict(VerdictKind.Other, "TRANSPORT ERROR", $"{ex.GetType().Name}: {ex.Message}");
    }
}

// The catalog comes from a raw GET /v1/models rather than the SDK: I could not verify the
// shape of a Models service on Anthropic 12.40 against the assembly. If client.Models.List()
// exists in your version, this method is the one place to swap it in.
async Task<List<string>> BuildProbeSetAsync(string url, string cred, bool oauth, bool beta)
{
    var set = new List<string>();

    using var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/") };
    http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");
    if (oauth)
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {cred}");
        if (beta) { http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader); }
    }
    else
    {
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", cred);
    }

    string? afterId = null;
    try
    {
        do
        {
            var query = "v1/models?limit=100" + (afterId is null ? "" : $"&after_id={afterId}");
            using var response = await http.GetAsync(query);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"catalog GET /v1/models -> {(int)response.StatusCode}; falling back to the built-in candidate list.");
                Console.WriteLine();
                afterId = null;
                break;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in data.EnumerateArray())
                {
                    if (entry.TryGetProperty("id", out var id) && id.GetString() is { } value) { set.Add(value); }
                }
            }

            afterId = root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True
                && root.TryGetProperty("last_id", out var last) ? last.GetString() : null;
        }
        while (afterId is not null);

        if (set.Count > 0)
        {
            Console.WriteLine($"catalog GET /v1/models -> {set.Count} model(s) visible to this credential.");
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"catalog GET /v1/models failed ({ex.GetType().Name}: {ex.Message}); falling back to the built-in candidate list.");
        Console.WriteLine();
    }

    // Candidates are probed even when the catalog lists them: being listed is not the same
    // as being entitled, which is the entire question this script answers.
    set.AddRange([
        "claude-opus-5",
        "claude-sonnet-5",
        "claude-fable-5",
        "claude-haiku-4-5",
        "claude-opus-4-8",
    ]);

    foreach (var name in new[] { "GRIMOIRE_INGEST_MODEL", "GRIMOIRE_QUERY_MODEL", "GRIMOIRE_LINT_MODEL" })
    {
        if (Env(name) is { } configured) { set.Add(configured); }
    }

    return set.Distinct(StringComparer.Ordinal).ToList();
}

static (string ErrorType, string Message) ParseError(string? responseBody)
{
    if (string.IsNullOrWhiteSpace(responseBody)) { return ("(no body)", ""); }

    try
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            return (
                error.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "");
        }
    }
    catch (JsonException) { }

    var oneLine = responseBody.Replace('\n', ' ').Trim();
    return ("(unparsed)", oneLine.Length > 120 ? oneLine[..120] + "..." : oneLine);
}

static string? Env(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static string Mask(string secret)
    => secret.Length <= 12 ? "***" : $"{secret[..10]}...{secret[^4..]} ({secret.Length} chars)";

enum VerdictKind { Available, NotEntitled, RateLimited, Auth, UnknownModel, Other }

record Verdict(VerdictKind Kind, string Label, string Detail);

/// <summary>
/// Adds the OAuth beta header and captures the raw response headers of the last call.
/// The captured headers are what make the diagnosis possible: AnthropicApiException exposes
/// the status and the body, but the anthropic-ratelimit-unified-* headers that separate a
/// real rate limit from an entitlement gap live only on the HTTP response.
/// </summary>
sealed class ProbeHandler(bool sendBeta) : DelegatingHandler
{
    private readonly List<KeyValuePair<string, string>> _lastHeaders = [];

    public void Reset() => _lastHeaders.Clear();

    public IReadOnlyList<KeyValuePair<string, string>> UnifiedRateLimitHeaders() => _lastHeaders;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (sendBeta && !request.Headers.Contains("anthropic-beta"))
        {
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        }

        var response = await base.SendAsync(request, cancellationToken);

        _lastHeaders.Clear();
        foreach (var header in response.Headers)
        {
            if (header.Key.StartsWith("anthropic-ratelimit-unified", StringComparison.OrdinalIgnoreCase))
            {
                _lastHeaders.Add(new(header.Key.ToLowerInvariant(), string.Join(",", header.Value)));
            }
        }

        return response;
    }
}
