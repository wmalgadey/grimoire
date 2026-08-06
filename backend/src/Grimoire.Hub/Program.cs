using Grimoire.Hub.Cli;
using Grimoire.Hub.IngestSubmission;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.QuerySubmission;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.RemediationTasks;

// 018-hub-cli-commands (FR-011, ADR-020 D3): the single dispatch gate. --help/-h anywhere
// in args, or a bareword args[0] (any command-shaped first argument, not just a
// Grimoire.Hub.Cli.HubCliCommands catalog name — see ShouldDispatchToCli below), hands
// off to the Spectre CommandApp — before ANY startup side effect (path resolution,
// secrets loading, SQLite init). Otherwise the web-host path below runs completely
// unchanged (ADR-009 precedence, PathSwitchCatalog untouched, app.Run() still binds the
// port).
if (ShouldDispatchToCli(args))
{
    return await HubCliApp.RunAsync(args);
}

var app = await HubHostComposition.BuildAsync(args);

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
app.Run();
return 0;

// 018-hub-cli-commands (FR-011): --help/-h wins over everything, including a command name
// (017's precedence rule, generalized). Otherwise, args[0] decides: a bareword (no leading
// "-") is always an attempted command invocation, dispatched to the CommandApp even when
// it names no catalog entry — Spectre's own unknown-command error then satisfies the
// contract's "unknown command name -> usage error, exit 2" edge case (research.md D3,
// contracts/cli-commands.md "Global rules"). A leading "-" (e.g. --base-dir) is always a
// server path switch, so the web-host path runs unchanged — matching pre-018 behavior for
// every invocation shape that isn't a command name.
static bool ShouldDispatchToCli(string[] args)
{
    if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase)
        || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
    {
        return true;
    }

    return args.Length > 0 && !args[0].StartsWith('-');
}
