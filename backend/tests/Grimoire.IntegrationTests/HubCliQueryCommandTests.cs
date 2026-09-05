using Grimoire.Hub.Cli;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.QuerySubmission;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T034 (018-hub-cli-commands, US4): the <c>query</c> command's full contract matrix
/// (specs/018-hub-cli-commands/contracts/cli-commands.md "query") — completed turn
/// (generated and supplied conversation id) with the header + verbatim multi-line answer
/// on stdout, a scripted failing turn, usage errors (no side effect), the three conflict
/// shapes (concurrency limit, conversation already active, record unreadable), and
/// SC-006's timeout-vs-cancellation distinction: both interrupt the turn via
/// <see cref="QueryRunCoordinator.InterruptAsync"/> before the command exits, with
/// mutually distinct messages and exit codes.
///
/// Exercises the production <see cref="QueryCommand"/> class directly against a real
/// <see cref="QueryRunCoordinator"/>/<see cref="QueryConversationRecordStore"/> and a real
/// (unconnected) SignalR hub context for <see cref="Grimoire.Hub.Realtime.QueryLifecyclePublisher"/>
/// — mirroring <see cref="HubCliRemediationTestHarness"/>'s idiom in
/// <c>HubCliCommandTests.cs</c> — invoked through the public <see cref="ICommand{TSettings}"/>
/// interface, capturing stdout/stderr via injected writers instead of the process-global
/// <see cref="Console"/>.
/// </summary>
public class HubCliQueryCommandTests
{
    [Fact]
    public async Task Query_NoConversationIdSupplied_Completes_GeneratesConformingId_PrintsHeaderAndAnswer_ExitZero()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true) { ScriptedAnswerChunks = [("The wiki says so.", TimeSpan.Zero)] };
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);

        var (exitCode, stdout, _) = await harness.RunQueryCommandAsync("What does the wiki say?");

        var request = Assert.Single(launcher.QueryRequests);
        Assert.Matches("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", request.ConversationId);

        Assert.Equal((int)CliExitCode.Success, exitCode);
        var lines = NormalizeLines(stdout);
        Assert.Equal($"Query turn {request.TurnId} in conversation {request.ConversationId}: completed", lines[0]);
        Assert.Equal("The wiki says so.", string.Join('\n', lines[1..]));
    }

    [Fact]
    public async Task Query_SuppliedConversationId_Completes_PrintsHeaderAndVerbatimMultilineAnswer_ExitZero()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("Line one.\n", TimeSpan.Zero), ("Line two.", TimeSpan.FromMilliseconds(20))],
        };
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);
        const string conversationId = "2026-08-01-query-clisupplied";

        var (exitCode, stdout, _) = await harness.RunQueryCommandAsync("Multi-line please?", conversationId);

        var turnId = Assert.Single(launcher.QueryRequests).TurnId;
        Assert.Equal((int)CliExitCode.Success, exitCode);
        var lines = NormalizeLines(stdout);
        Assert.Equal($"Query turn {turnId} in conversation {conversationId}: completed", lines[0]);
        Assert.Equal("Line one.\nLine two.", string.Join('\n', lines[1..]));
    }

    [Fact]
    public async Task Query_StreamsOnlyTheAnswerDelta_NotTheWholeAccumulatedAnswerOnEachPoll()
    {
        // Regression guard for T033's delta-streaming requirement: if the command
        // re-streamed the whole accumulated Answer on every poll instead of just the
        // newly appended text, concatenating everything written to stderr would contain
        // duplicated prefixes and would NOT equal the final answer text exactly.
        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("First chunk. ", TimeSpan.Zero), ("Second chunk.", TimeSpan.FromMilliseconds(120))],
        };
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);

        var (exitCode, _, stderr) = await harness.RunQueryCommandAsync("Stream this?", "2026-08-01-query-clistream");

        Assert.Equal((int)CliExitCode.Success, exitCode);
        var streamedConcatenated = stderr.Replace("\r\n", "\n").Replace("\n", "");
        Assert.Equal("First chunk. Second chunk.", streamedConcatenated);
    }

    [Fact]
    public async Task Query_ScriptedFailingTurn_PrintsFailureReason_ExitOne()
    {
        var launcher = new FakeAgentProcessLauncher(terminalStatus: "failed", failureReason: "The query agent could not answer.");
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);
        const string conversationId = "2026-08-01-query-clifailed";

        var (exitCode, stdout, _) = await harness.RunQueryCommandAsync("Will this fail?", conversationId);

        var turnId = Assert.Single(launcher.QueryRequests).TurnId;
        Assert.Equal((int)CliExitCode.OperationFailed, exitCode);
        Assert.Equal($"Query turn {turnId} failed: The query agent could not answer.", stdout.Trim());
    }

    // ── usage errors (T032, FR-009/FR-018): Spectre's real CommandApp pipeline calls
    // Settings.Validate() before ExecuteAsync ever runs (mapped to exit 2 by
    // HubCliApp) — these prove the validation half of that contract directly, mirroring
    // RemediationTaskSettings_Validate_MissingOrEmptyTaskId_IsUsageError in
    // HubCliCommandTests.cs.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void QuerySettings_Validate_MissingOrEmptyPrompt_IsUsageError(string? prompt)
    {
        var settings = new QuerySettings { Prompt = prompt };
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void QuerySettings_Validate_PromptOverMaxLength_IsUsageError()
    {
        var settings = new QuerySettings { Prompt = new string('a', QuerySubmissionValidator.PromptMaxLength + 1) };
        Assert.False(settings.Validate().Successful);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("-leading-dash")]
    [InlineData("_leading-underscore")]
    public void QuerySettings_Validate_MalformedConversationId_IsUsageError(string conversationId)
    {
        var settings = new QuerySettings { Prompt = "Valid prompt", ConversationId = conversationId };
        Assert.False(settings.Validate().Successful);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuerySettings_Validate_NonPositiveTimeout_IsUsageError(int timeout)
    {
        var settings = new QuerySettings { Prompt = "Valid prompt", Timeout = timeout };
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void QuerySettings_Validate_WellFormedSettings_Succeeds()
    {
        var settings = new QuerySettings { Prompt = "Valid prompt", ConversationId = "c-1", Timeout = 10 };
        Assert.True(settings.Validate().Successful);
    }

    // ── conflicts (T032/T033, FR-007): each printed message names the specific reason,
    // distinct from every other message (SC-006), and matches
    // contracts/cli-commands.md "query" exactly.

    [Fact]
    public async Task Query_ConcurrencyLimitReached_PrintsMessage_ExitFour()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher, concurrencyLimit: 1);
        var fillResult = await harness.Coordinator.SubmitTurnAsync("2026-08-01-query-clilimit-filler", "Filler prompt");
        Assert.IsType<QuerySubmissionResult.Accepted>(fillResult);

        var (exitCode, stdout, _) = await harness.RunQueryCommandAsync("One too many?", "2026-08-01-query-clilimit-overflow");

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal("The Hub is at its query concurrency limit; try again later.", stdout.Trim());
    }

    [Fact]
    public async Task Query_ConversationAlreadyActive_PrintsMessage_ExitFour()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);
        const string conversationId = "2026-08-01-query-cliactive";
        var firstResult = await harness.Coordinator.SubmitTurnAsync(conversationId, "First prompt");
        Assert.IsType<QuerySubmissionResult.Accepted>(firstResult);

        var (exitCode, stdout, _) = await harness.RunQueryCommandAsync("Second prompt?", conversationId);

        Assert.Equal((int)CliExitCode.StateConflict, exitCode);
        Assert.Equal($"Conversation {conversationId} already has an active turn.", stdout.Trim());
    }

    [Fact]
    public async Task Query_ConversationRecordUnreadable_PrintsMessage_ExitOne_NoTurnSubmitted()
    {
        using var harness = await HubCliQueryTestHarness.CreateAsync();
        const string conversationId = "2026-08-01-query-cliunreadable";
        var recordPath = harness.Paths.ConversationRecordPathFor(conversationId);
        await File.WriteAllTextAsync(recordPath, "not a valid conversation record");

        var (exitCode, stdout, _) = await harness.RunQueryCommandAsync("Will this be readable?", conversationId);

        Assert.Equal((int)CliExitCode.OperationFailed, exitCode);
        Assert.StartsWith(
            $"Conversation record for {conversationId} is unreadable:", stdout.Trim(), StringComparison.Ordinal);
        Assert.Empty(harness.Launcher.QueryRequests);
    }

    // ── timeout vs. cancellation (T033, SC-006): both interrupt the turn before the
    // command exits, but with distinct messages/exit codes.

    [Fact]
    public async Task Query_TimesOut_InterruptsTurn_PrintsDistinctTimeoutMessage_ExitFive_PartialAnswerPersisted()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);
        const string conversationId = "2026-08-01-query-clitimeout";

        var commandTask = harness.RunQueryCommandAsync("Timeout please?", conversationId, timeoutSeconds: 1);

        await WaitUntilAsync(() => launcher.Handles.Count == 1);
        var handle = launcher.Handles[0];
        handle.EmitEvent("started", "irrelevant");
        handle.EmitEvent("answer_chunk", "irrelevant", new { text = "Partial answer chunk." });

        var (exitCode, stdout, stderr) = await commandTask;
        var turnId = Assert.Single(launcher.QueryRequests).TurnId;

        Assert.Equal((int)CliExitCode.WaitTimeout, exitCode);
        Assert.Equal(
            $"Timed out after 1s waiting for query turn {turnId}; the turn was interrupted and its partial answer persisted.",
            stdout.Trim());
        Assert.Contains("Partial answer chunk.", stderr, StringComparison.Ordinal);

        // InterruptAsync actually ran: the turn reached its terminal `interrupted` state
        // with the partial answer preserved (FR-015/FR-016 — no agent work continues
        // unsupervised after the CLI exits).
        var turn = harness.Coordinator.GetTurn(turnId);
        Assert.NotNull(turn);
        Assert.Equal(QueryTurnStatus.Interrupted, turn!.Status);
        Assert.Equal("Partial answer chunk.", turn.Answer);
    }

    [Fact]
    public async Task Query_CancelledMidWait_InterruptsTurn_PrintsDistinctCancelledMessage_ExitOneThirty()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);
        const string conversationId = "2026-08-01-query-clicancel";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var (exitCode, stdout, _) = await harness.RunQueryCommandAsync(
            "Cancel please?", conversationId, timeoutSeconds: 300, cancellationToken: cts.Token);
        var turnId = Assert.Single(launcher.QueryRequests).TurnId;

        Assert.Equal((int)CliExitCode.Cancelled, exitCode);
        Assert.Equal($"Cancelled: query turn {turnId} interrupted.", stdout.Trim());

        var turn = harness.Coordinator.GetTurn(turnId);
        Assert.NotNull(turn);
        Assert.Equal(QueryTurnStatus.Interrupted, turn!.Status);
    }

    /// <summary>Splits on '\n' after normalizing '\r\n', so line-based assertions don't depend on the host's newline convention.</summary>
    private static string[] NormalizeLines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000) =>
        PollAsync.WaitAsync(
            condition,
            TimeSpan.FromMilliseconds(timeoutMs),
            "Condition was not met within the timeout.",
            pollInterval: TimeSpan.FromMilliseconds(20));
}

/// <summary>
/// Hermetic <see cref="QueryRunCoordinator"/> + CLI command harness (018-hub-cli-commands
/// T034), mirroring <c>HubCliRemediationTestHarness</c>'s idiom (a real, unconnected
/// SignalR hub context for the lifecycle publisher) for <see cref="QueryCommand"/>: a real
/// <see cref="QueryConversationRecordStore"/>/<see cref="QueryRunCoordinator"/> and a
/// scriptable <see cref="FakeAgentProcessLauncher"/>. Invokes the production command
/// class via <see cref="ICommand{TSettings}"/> directly, capturing stdout/stderr via
/// injected writers instead of the process-global <see cref="Console"/>.
/// </summary>
internal sealed class HubCliQueryTestHarness : IDisposable
{
    private readonly string _root;
    private readonly WebApplication _hubHost;

    private HubCliQueryTestHarness(
        string root, WebApplication hubHost, ResolvedGrimoirePaths paths, FakeAgentProcessLauncher launcher,
        QueryConversationRecordStore recordStore, QueryRunCoordinator coordinator)
    {
        _root = root;
        _hubHost = hubHost;
        Paths = paths;
        Launcher = launcher;
        RecordStore = recordStore;
        Coordinator = coordinator;
    }

    public ResolvedGrimoirePaths Paths { get; }
    public FakeAgentProcessLauncher Launcher { get; }
    public QueryConversationRecordStore RecordStore { get; }
    public QueryRunCoordinator Coordinator { get; }

    public static async Task<HubCliQueryTestHarness> CreateAsync(
        FakeAgentProcessLauncher? launcher = null, int concurrencyLimit = 3, TimeSpan? livenessWindow = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-hub-cli-query-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.ConversationsDir);

        var effectiveLauncher = launcher ?? new FakeAgentProcessLauncher();
        var recordStore = new QueryConversationRecordStore(paths);

        // Real (unconnected) SignalR hub context, mirroring HubCliRemediationTestHarness/
        // RemediationObservabilityTests.StartHubHostAsync — publishing never needs an
        // actual connected client for these tests.
        var hubHostBuilder = WebApplication.CreateBuilder();
        hubHostBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        hubHostBuilder.Services.AddSignalR();
        var hubHost = hubHostBuilder.Build();
        hubHost.MapHub<QueryLifecycleHub>("/hubs/query-lifecycle");
        await hubHost.StartAsync();

        var publisher = new QueryLifecyclePublisher(
            hubHost.Services.GetRequiredService<IHubContext<QueryLifecycleHub>>(),
            NullLogger<QueryLifecyclePublisher>.Instance);

        var coordinator = new QueryRunCoordinator(
            effectiveLauncher, publisher, recordStore, paths,
            new QueryConcurrencyOptions { QueryConcurrencyLimit = concurrencyLimit },
            livenessWindow: livenessWindow,
            logger: NullLogger<QueryRunCoordinator>.Instance);

        return new HubCliQueryTestHarness(root, hubHost, paths, effectiveLauncher, recordStore, coordinator);
    }

    public async Task<(int ExitCode, string Stdout, string Stderr)> RunQueryCommandAsync(
        string? prompt, string? conversationId = null, int timeoutSeconds = 300,
        CancellationToken cancellationToken = default, TimeProvider? timeProvider = null)
    {
        var stderrWriter = new StringWriter();
        var stderrConsole = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(stderrWriter) });
        var status = new CliStatusRenderer(stderrConsole);
        var stdoutWriter = new StringWriter();

        var command = new QueryCommand(Coordinator, status, stdoutWriter, timeProvider ?? TimeProvider.System);
        var context = new CommandContext(Array.Empty<string>(), EmptyRemainingArguments.Instance, "query", null);
        var settings = new QuerySettings { Prompt = prompt, ConversationId = conversationId, Timeout = timeoutSeconds };

        var exitCode = await ((ICommand<QuerySettings>)command).ExecuteAsync(context, settings, cancellationToken);

        return (exitCode, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    public void Dispose()
    {
        _hubHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }

    private sealed class EmptyRemainingArguments : IRemainingArguments
    {
        public static readonly EmptyRemainingArguments Instance = new();

        public ILookup<string, string?> Parsed { get; } = Array.Empty<string>().ToLookup(s => s, s => (string?)null);

        public IReadOnlyList<string> Raw { get; } = [];
    }
}
