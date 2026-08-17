using System.Net;
using Grimoire.Hub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Grimoire.IntegrationTests;

/// <summary>
/// The Hub serves the built frontend itself, so the deployment is one container and anything
/// put in front of it is ordinary infrastructure with one upstream — it needs no knowledge of
/// Grimoire's routes.
///
/// <para>
/// What is asserted here is our own routing decision, not ASP.NET Core's static-file middleware
/// (Constitution Principle II, "Test what we own"): that a deep link reaches the SPA document,
/// that an unmatched backend path does <em>not</em>, and that a real endpoint still wins over
/// both. The middle one is the reason this file exists — <c>MapFallbackToFile</c> catches
/// everything, so without the explicit backend fallbacks a mistyped <c>/api/…</c> would answer
/// the SPA with HTTP 200, and every caller that checks status codes would see success.
/// </para>
///
/// <para>
/// Hosted against a real request pipeline over a real temporary directory — the production
/// <see cref="HubEndpoints.MapSingleOriginFrontend"/> is what is under test, not a re-creation
/// of it.
/// </para>
/// </summary>
public sealed class HubFrontendHostingTests : IDisposable
{
    private const string IndexMarkup = "<!doctype html><title>Grimoire SPA</title>";

    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), $"grimoire-frontend-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DeepLink_ReachesTheSpaDocument_SoAColdLoadOfATaskUrlWorks()
    {
        using var host = await BuildHostAsync(withFrontend: true);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/tasks/2026-08-01-ingest-something");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(IndexMarkup, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Root_ServesTheSpaDocument_RatherThanTheBareGreeting()
    {
        using var host = await BuildHostAsync(withFrontend: true);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");

        // The entry point every operator types. It answered the plain-text greeting in a first
        // cut of this feature, because a mapped "/" endpoint wins over the static-file
        // middleware — so this asserts the document, not merely a 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(IndexMarkup, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutABuiltFrontend_RootStillAnswersTheGreeting()
    {
        using var host = await BuildHostAsync(withFrontend: false);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Grimoire Hub", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/not-a-real-endpoint")]
    [InlineData("/api/ingest-submissions/typo/deeper")]
    [InlineData("/hubs/not-a-real-hub")]
    public async Task UnmatchedBackendPath_Is404_AndNeverTheSpaDocument(string path)
    {
        using var host = await BuildHostAsync(withFrontend: true);
        var client = host.GetTestClient();

        var response = await client.GetAsync(path);

        // A client calling a path we do not serve has to learn that from the status code. The
        // SPA answering 200 here is the failure this test exists to prevent.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(IndexMarkup, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // And it answers through the one envelope every Hub failure carries (ADR-026), rather
        // than the bare 404 an unrouted path produced before the frontend moved in. The
        // envelope's own shape is HubApiErrorEnvelopeTests' subject, not this file's.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MappedEndpoint_StillAnswers_WithTheFrontendMounted()
    {
        using var host = await BuildHostAsync(withFrontend: true);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/probe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("probed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithoutABuiltFrontend_TheHubStillServes_SoRunningFromSourceIsUnaffected()
    {
        // `dotnet run` against a checkout has no built frontend — that is `bun run dev` beside
        // it, and VS Code's `prod` launch config too. Mounting must be opt-in on the fallback
        // document, never a startup requirement.
        using var host = await BuildHostAsync(withFrontend: false);
        var client = host.GetTestClient();

        var probe = await client.GetAsync("/api/probe");
        var deepLink = await client.GetAsync("/tasks/anything");

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deepLink.StatusCode);
    }

    private async Task<IHost> BuildHostAsync(bool withFrontend)
    {
        Directory.CreateDirectory(_contentRoot);
        if (withFrontend)
        {
            var webRoot = Path.Combine(_contentRoot, "wwwroot");
            Directory.CreateDirectory(webRoot);
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), IndexMarkup);
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = _contentRoot,
            WebRootPath = "wwwroot"
        });
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        // Mirrors HubEndpoints.MapGrimoireEndpoints, including the greeting it maps only when
        // no frontend is present. Getting that condition wrong is invisible without it: routing
        // picks the endpoint before the static-file middleware runs, so a "/" endpoint mapped
        // unconditionally makes the root answer plain text while every other path serves the app.
        var frontendMounted = app.MapSingleOriginFrontend();
        if (!frontendMounted)
        {
            app.MapGet("/", () => "Grimoire Hub");
        }

        app.MapGet("/api/probe", () => Results.Text("probed"));

        await app.StartAsync();
        return app;
    }
}
