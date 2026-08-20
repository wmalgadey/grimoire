using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests.TestSupport;

/// <summary>
/// A real Kestrel listener standing in for the model provider, shared by the model-adapter
/// tests (#119/#120/#122/#123/#127). The adapter under test is pointed at it through the
/// same <c>GRIMOIRE_*_BASE_URL</c> an operator would use, so what runs is actual HTTP
/// through the actual SDK — Principle II's "real infrastructure", with no mocked
/// <see cref="HttpMessageHandler"/> anywhere in the path.
/// <para>
/// Every request body the adapter sends is recorded in <see cref="Requests"/>, which is
/// what lets a test assert on the wire what the adapter <em>put</em> in the request
/// (<c>max_tokens</c>, <c>strict</c>) rather than on the shape of an object it built.
/// </para>
/// </summary>
public sealed class FakeAnthropicEndpoint : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly List<string> _requests = [];
    private readonly Lock _gate = new();
    private readonly Func<int, (HttpStatusCode Status, string Body)> _respond;
    private readonly string _contentType;
    private int _requestCount;

    private FakeAnthropicEndpoint(
        WebApplication app,
        string baseUrl,
        Func<int, (HttpStatusCode, string)> respond,
        string contentType)
    {
        _app = app;
        BaseUrl = baseUrl;
        _respond = respond;
        _contentType = contentType;
    }

    /// <summary>The listener's base URL, for <c>GRIMOIRE_&lt;AGENT&gt;_BASE_URL</c>.</summary>
    public string BaseUrl { get; }

    /// <summary>Every request body received, in arrival order.</summary>
    public IReadOnlyList<string> Requests
    {
        get { lock (_gate) { return [.. _requests]; } }
    }

    /// <summary>Answers every request with the same status and body.</summary>
    public static Task<FakeAnthropicEndpoint> StartAsync(
        HttpStatusCode status, string body, string contentType = "application/json")
        => StartAsync(_ => (status, body), contentType);

    /// <summary>
    /// Answers request <em>n</em> (zero-based) with whatever <paramref name="respond"/>
    /// returns for it — for tests that need the first call to differ from the retries.
    /// </summary>
    public static async Task<FakeAnthropicEndpoint> StartAsync(
        Func<int, (HttpStatusCode Status, string Body)> respond,
        string contentType = "application/json")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");

        var app = builder.Build();

        FakeAnthropicEndpoint? endpoint = null;

        // Catch-all: whatever path the SDK composes under the base URL answers the same.
        app.Run(async context =>
        {
            var (status, body) = endpoint!.RecordAndResolve(
                await new StreamReader(context.Request.Body).ReadToEndAsync());

            context.Response.StatusCode = (int)status;
            context.Response.ContentType = endpoint.ContentTypeForResponses;
            await context.Response.WriteAsync(body);
        });

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        endpoint = new FakeAnthropicEndpoint(app, address, respond, contentType);
        return endpoint;
    }

    private string ContentTypeForResponses => _contentType;

    private (HttpStatusCode Status, string Body) RecordAndResolve(string requestBody)
    {
        int index;
        lock (_gate)
        {
            _requests.Add(requestBody);
            index = _requestCount++;
        }

        return _respond(index);
    }

    /// <summary>A provider-shaped error envelope: <c>{"type":"error","error":{…}}</c>.</summary>
    public static string ErrorBody(string errorType, string message)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "error",
            error = new { type = errorType, message },
        });

    /// <summary>
    /// A successful (HTTP 200) Messages response that stops for
    /// <paramref name="stopReason"/>, optionally carrying the <c>stop_details</c> the API
    /// sends alongside <c>stop_reason: "refusal"</c>.
    /// </summary>
    public static string MessageBody(
        string stopReason,
        string? text = null,
        string? refusalCategory = null,
        string? refusalExplanation = null)
    {
        var stopDetails = refusalCategory is null && refusalExplanation is null
            ? null
            : new Dictionary<string, object?>
            {
                ["type"] = "refusal",
                ["category"] = refusalCategory,
                ["explanation"] = refusalExplanation,
            };

        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = "msg_fake",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "fake-model",
            ["content"] = text is null
                ? Array.Empty<object>()
                : [new { type = "text", text }],
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["stop_details"] = stopDetails,
            ["usage"] = new { input_tokens = 11, output_tokens = 7 },
        });
    }

    /// <summary>
    /// The streaming counterpart of <see cref="MessageBody"/>: the SSE event sequence the
    /// Messages API sends when the request is streamed, as
    /// <c>text/event-stream</c>. Serve it with
    /// <c>StartAsync(status, StreamingMessageBody(...), StreamingContentType)</c>.
    /// </summary>
    public const string StreamingContentType = "text/event-stream";

    /// <summary>One complete streamed turn: a single text block, then a clean stop.</summary>
    public static string StreamingMessageBody(string text, string stopReason = "end_turn")
    {
        static string Event(string name, object payload)
            => $"event: {name}\ndata: {System.Text.Json.JsonSerializer.Serialize(payload)}\n\n";

        return
            Event("message_start", new
            {
                type = "message_start",
                message = new
                {
                    id = "msg_fake",
                    type = "message",
                    role = "assistant",
                    model = "fake-model",
                    content = Array.Empty<object>(),
                    stop_reason = (string?)null,
                    stop_sequence = (string?)null,
                    usage = new { input_tokens = 11, output_tokens = 0 },
                },
            }) +
            Event("content_block_start", new
            {
                type = "content_block_start",
                index = 0,
                content_block = new { type = "text", text = "" },
            }) +
            Event("content_block_delta", new
            {
                type = "content_block_delta",
                index = 0,
                delta = new { type = "text_delta", text },
            }) +
            Event("content_block_stop", new { type = "content_block_stop", index = 0 }) +
            Event("message_delta", new
            {
                type = "message_delta",
                delta = new { stop_reason = stopReason, stop_sequence = (string?)null },
                usage = new { output_tokens = 7 },
            }) +
            Event("message_stop", new { type = "message_stop" });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
