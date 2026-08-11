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
        app.MapGet("/", () => "Grimoire Hub");
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
}
