using Grimoire.Hub.ApiErrors;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.QuerySubmission;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.RemediationTasks;

/// <summary>
/// The Hub's HTTP/SignalR surface, mapped onto an already-built <see cref="WebApplication"/>.
/// Extracted from <c>Program.cs</c>'s top-level statements when the web host became a Spectre
/// default command (<see cref="Grimoire.Hub.Cli.HubRootCommand"/>): the mappings needed a
/// callable home once the server path stopped being straight-line startup code.
///
/// Declared in the global namespace for the same reason <see cref="HubHostComposition"/> is —
/// it is composition-root code, deliberately outside the <c>Grimoire.Hub.*</c> namespaces the
/// architecture rules filter on.
///
/// Called only from the server path, never from <see cref="HubHostComposition.BuildAsync"/>:
/// the one-shot CLI commands build the same composition but never serve HTTP, and should not
/// pay for (or imply) an endpoint surface they never expose.
/// </summary>
internal static class HubEndpoints
{
    public static WebApplication MapGrimoireEndpoints(this WebApplication app)
    {
        // 024-api-error-presentation (ADR-026): first in the pipeline, so an exception escaping any
        // endpoint below answers with the same envelope as a deliberate rejection instead of the
        // bare, empty-bodied 500 Kestrel produced before this feature. The handler itself is
        // registered in HubHostComposition; the middleware has to sit in the app pipeline, which
        // only exists on the server path.
        app.UseExceptionHandler();

        var frontendMounted = app.MapSingleOriginFrontend();

        // Only when there is no frontend to serve. Routing selects an endpoint before the
        // static-file middleware runs, and that middleware deliberately stands down once an
        // endpoint is already selected — so leaving this mapped would make "/" answer the bare
        // greeting while every other path served the app.
        if (!frontendMounted)
        {
            app.MapGet("/", () => "Grimoire Hub");
        }
        app.MapHub<IngestLifecycleHub>("/hubs/ingest-lifecycle");
        app.MapGroup("/api/ingest-submissions").MapIngestSubmissionEndpoints();
        app.MapGroup("/api/ingest-queue").MapIngestQueueEndpoints();
        app.MapHub<QueryLifecycleHub>("/hubs/query-lifecycle");
        app.MapGroup("/api/query-conversations").MapQueryConversationEndpoints();
        app.MapGroup("/api/query-turns").MapQueryTurnEndpoints();
        app.MapHub<LintLifecycleHub>("/hubs/lint-lifecycle");
        app.MapGroup("/api/lint-runs").MapLintRunEndpoints();
        // 015-lint-board-parity T012: composite board initial state (contracts/lint-board-api.md).
        app.MapGroup("/api/board").MapBoardEndpoints();
        // 015-lint-board-parity T023/T024: remediation task lifecycle channel + read endpoints
        // (contracts/remediation-lifecycle-events.md "Hub 2", contracts/remediation-task-api.md).
        app.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
        app.MapGroup("/api/remediation-tasks").MapRemediationTaskEndpoints();
        return app;
    }

    /// <summary>
    /// Serves the built frontend from the Hub itself, so a deployment is one container and any
    /// proxy in front of it is ordinary infrastructure with a single upstream rather than
    /// something that has to know Grimoire's route layout.
    ///
    /// <para>
    /// Opt-in on the fallback document existing. The SPA is copied to <c>wwwroot/</c> beside the
    /// Hub assembly by the deployment image build; a checkout run with <c>dotnet run</c> has none
    /// and is served by <c>vite dev</c> instead, so this must stay a no-op there rather than a
    /// startup failure. The test is <c>index.html</c> rather than the directory precisely because
    /// the directory is not a reliable signal — <c>WebApplicationBuilder</c> creates the web root
    /// when it is configured, so an empty <c>wwwroot/</c> would otherwise mount a frontend that
    /// does not exist and answer every path with 404.
    /// </para>
    ///
    /// <para>
    /// The two explicit fallbacks are the load-bearing part. <c>MapFallbackToFile</c> catches
    /// every unmatched path, which would answer a mistyped <c>/api/…</c> with the SPA document
    /// and HTTP 200 — turning a client bug into a blank screen and hiding it from anything that
    /// checks status codes. Both share the file fallback's order, so ASP.NET Core's route
    /// precedence (a literal segment beats a catch-all) is what selects them; the behaviour is
    /// pinned by <c>HubFrontendHostingTests</c>.
    /// </para>
    /// </summary>
    /// <returns><see langword="true"/> when a built frontend was found and mounted.</returns>
    internal static bool MapSingleOriginFrontend(this WebApplication app)
    {
        var fallbackDocument = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
        if (!File.Exists(fallbackDocument))
        {
            return false;
        }

        // Before routing, so "/" and every hashed asset are served without reaching an endpoint.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapFallbackToFile("index.html");

        // Unmatched backend paths answer 404 through the one envelope every Hub failure
        // carries (ADR-026 BR1) rather than the bare, empty-bodied 404 an unrouted path used
        // to produce — a client that mistyped a path gets the same shape it parses everywhere
        // else, and `Results.NotFound()` here would be exactly the inline error result that
        // rule exists to prevent.
        app.MapFallback("/api/{**rest}", () => ApiErrorResults.Problem(ApiErrorCatalogue.EndpointNotFound));
        app.MapFallback("/hubs/{**rest}", () => ApiErrorResults.Problem(ApiErrorCatalogue.EndpointNotFound));

        return true;
    }
}
