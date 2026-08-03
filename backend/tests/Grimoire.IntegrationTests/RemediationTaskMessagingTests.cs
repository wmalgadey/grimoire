using System.Net;
using System.Text;
using System.Text.Json;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T040 (015-lint-board-parity, US5, ADR-018) — attach-context (Proposed-only, 409
/// otherwise, FR-011) appends a context entry to the record and reaches the execution
/// request's user-prompt override once the task is authorized and dispatched; a message
/// turn (202) spawns a fake message-turn run whose reply is appended (<c>sender:
/// agent</c>) and broadcast on <c>remediationMessageTurnChanged</c>; the history endpoint
/// returns the full thread including after a terminal outcome (FR-014); and prior
/// messages are included in the next message-turn's context (record-as-context, R6).
/// Hermetic — fake <c>IAgentProcessLauncher</c>, real SQLite + real SignalR (bound to a
/// real Kestrel host so a genuine SignalR client can observe broadcasts, mirroring
/// <c>LintRemediationProposalMaterializationTests</c>), no live LLM.
/// </summary>
public class RemediationTaskMessagingTests
{
    // ── attach-context (FR-011) ────────────────────────────────────────────────────

    [Fact]
    public async Task AttachContext_WhileProposed_Returns200_AndAppearsInDetail()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/context",
            new { content = "Use the tag taxonomy from index.md, not free-form tags." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(taskId, body.RootElement.GetProperty("taskId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("attachedAt").GetString()));

        var detail = await harness.Client.GetAsync($"/api/remediation-tasks/{taskId}");
        using var detailBody = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var attachedContext = detailBody.RootElement.GetProperty("attachedContext");
        Assert.Equal(1, attachedContext.GetArrayLength());
        Assert.Equal(
            "Use the tag taxonomy from index.md, not free-form tags.",
            attachedContext[0].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(RemediationTaskStates.Authorized)]
    [InlineData(RemediationTaskStates.Dismissed)]
    public async Task AttachContext_WhileNotProposed_Returns409(string state)
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, state);

        var response = await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/context",
            new { content = "Too late to add context." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("task_not_proposed", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(state, body.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task AttachContext_EmptyContent_Returns400_AndAppendsNothing()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/context", new { content = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var parsed = Assert.IsType<RemediationTaskRecordParseResult.Parsed>(await harness.RecordStore.ReadAsync(taskId));
        Assert.DoesNotContain(parsed.Entries, e => e is RemediationTaskRecordEntry.Context);
    }

    [Fact]
    public async Task AttachedContext_ReachesTheExecutionRequests_UserPromptOverride_OnceAuthorizedAndDispatched()
    {
        // FR-011: attached context settles before authorization freezes what execution
        // will see — verified end to end by authorizing the task (which dispatches
        // immediately, autoPlay: false leaves the fake launcher's request inspectable)
        // and asserting AttachedContext carries the verbatim attached text.
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/context",
            new { content = "Use the tag taxonomy from index.md, not free-form tags." });

        var authorizeResponse = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/authorize", content: null);
        Assert.Equal(HttpStatusCode.OK, authorizeResponse.StatusCode);

        await harness.WaitForRemediationRequestAsync();
        var request = Assert.Single(harness.Launcher.RemediationRequests);
        Assert.Equal(taskId, request.TaskId);
        Assert.Equal("Use the tag taxonomy from index.md, not free-form tags.", request.AttachedContext);
    }

    // ── message turn (FR-012) ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WhileProposed_Returns202_AppendsTheHumanMessageImmediately_AndSpawnsATurn()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages",
            new { content = "Why does this page need the 'configuration' tag?" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(taskId, body.RootElement.GetProperty("taskId").GetString());
        Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("messageTurnId").GetString()));

        // The human message is appended to the record immediately (contract), before the
        // turn is even known to have completed.
        var parsed = Assert.IsType<RemediationTaskRecordParseResult.Parsed>(await harness.RecordStore.ReadAsync(taskId));
        var humanMessage = Assert.Single(parsed.Entries.OfType<RemediationTaskRecordEntry.Message>());
        Assert.Equal(RemediationTaskRecordFormat.SenderHuman, humanMessage.Sender);
        Assert.Equal("Why does this page need the 'configuration' tag?", humanMessage.Text);

        await harness.WaitForMessageTurnRequestAsync();
        var request = Assert.Single(harness.Launcher.MessageTurnRequests);
        Assert.Equal(taskId, request.TaskId);
        Assert.Equal("Why does this page need the 'configuration' tag?", request.Message);
        Assert.Empty(request.PriorMessages);
    }

    [Fact]
    public async Task MessageTurnCompletes_AppendsTheAgentsReply_WithSenderAgent_AndBroadcastsCompleted()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var received = new List<JsonElement>();
        var lockObj = new object();
        harness.Connection.On<JsonElement>("remediationMessageTurnChanged", e => { lock (lockObj) { received.Add(e); } });
        await harness.Connection.StartAsync();

        await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages",
            new { content = "Why does this page need the 'configuration' tag?" });

        await harness.WaitForMessageTurnRequestAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEventWithFields("completed", taskId, new Dictionary<string, object?>
        {
            ["summary"] = "It documents GrimoirePathOptions, which is configuration surface.",
            ["text"] = "It documents GrimoirePathOptions, which is configuration surface.",
        });

        RemediationTaskRecordParseResult.Parsed? parsed = null;
        await PollAsync.WaitAsync(
            async () =>
            {
                if (await harness.RecordStore.ReadAsync(taskId) is RemediationTaskRecordParseResult.Parsed p &&
                    p.Entries.OfType<RemediationTaskRecordEntry.Message>().Any(m => m.Sender == RemediationTaskRecordFormat.SenderAgent))
                {
                    parsed = p;
                    return true;
                }

                return false;
            },
            TimeSpan.FromSeconds(10),
            "Expected an agent message to appear on the Remediation Task Record within 10s.");

        Assert.NotNull(parsed);
        var messages = parsed!.Entries.OfType<RemediationTaskRecordEntry.Message>().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal(RemediationTaskRecordFormat.SenderHuman, messages[0].Sender);
        Assert.Equal(RemediationTaskRecordFormat.SenderAgent, messages[1].Sender);
        Assert.Equal("It documents GrimoirePathOptions, which is configuration surface.", messages[1].Text);

        await PollAsync.WaitAsync(
            () =>
            {
                lock (lockObj)
                {
                    return received.Any(e => e.GetProperty("state").GetString() == "completed");
                }
            },
            TimeSpan.FromSeconds(10),
            "Expected a 'completed' broadcast within 10s.");

        List<JsonElement> snapshot;
        lock (lockObj) { snapshot = [.. received]; }
        Assert.Contains(snapshot, e => e.GetProperty("taskId").GetString() == taskId && e.GetProperty("state").GetString() == "running");
        Assert.Contains(snapshot, e => e.GetProperty("taskId").GetString() == taskId && e.GetProperty("state").GetString() == "completed");
    }

    [Theory]
    [InlineData(RemediationTaskStates.Authorized)]
    [InlineData(RemediationTaskStates.Completed)]
    public async Task SendMessage_WhileNotProposed_Returns409(string state)
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, state);

        var response = await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages", new { content = "Question?" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("task_not_proposed", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task SendMessage_WhileATurnIsAlreadyActive_Returns409_MessageTurnActive()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var first = await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages", new { content = "First question?" });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await harness.WaitForMessageTurnRequestAsync();

        var second = await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages", new { content = "Second question?" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("message_turn_active", body.RootElement.GetProperty("reason").GetString());
    }

    // ── history (FR-014) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessages_UnknownTask_Returns404()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();

        var response = await harness.Client.GetAsync("/api/remediation-tasks/does-not-exist/messages");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_ForATaskWithNoMessagesYet_ReturnsAnEmptyArray_NotNotFound()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        var response = await harness.Client.GetAsync($"/api/remediation-tasks/{taskId}/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task History_RemainsReadable_AfterTheTaskReachesATerminalOutcome()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages", new { content = "Question before dismissal?" });
        await harness.WaitForMessageTurnRequestAsync();
        var handle = Assert.Single(harness.Launcher.Handles);
        handle.EmitEventWithFields("completed", taskId, new Dictionary<string, object?>
        {
            ["text"] = "Here is the answer.",
        });

        await PollAsync.WaitAsync(
            async () => await harness.RecordStore.ReadAsync(taskId) is RemediationTaskRecordParseResult.Parsed p &&
                p.Entries.OfType<RemediationTaskRecordEntry.Message>().Count() == 2,
            TimeSpan.FromSeconds(10),
            "Expected 2 messages on the Remediation Task Record within 10s.");

        // Drive the task to a terminal outcome (Dismissed, FR-010 — no agent involvement).
        var dismissResponse = await harness.Client.PostAsync($"/api/remediation-tasks/{taskId}/dismiss", content: null);
        Assert.Equal(HttpStatusCode.OK, dismissResponse.StatusCode);

        var historyResponse = await harness.Client.GetAsync($"/api/remediation-tasks/{taskId}/messages");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var body = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("human", messages[0].GetProperty("sender").GetString());
        Assert.Equal("agent", messages[1].GetProperty("sender").GetString());
        Assert.Equal("Here is the answer.", messages[1].GetProperty("content").GetString());
    }

    // ── record-as-context (R6) ──────────────────────────────────────────────────────

    [Fact]
    public async Task SecondMessageTurn_IncludesThePriorExchangeInItsContext()
    {
        await using var harness = await RemediationMessagingHarness.CreateAsync();
        const string taskId = "2026-08-01-remediation-a1b2c3";
        await harness.InsertTaskAsync(taskId, RemediationTaskStates.Proposed);

        await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages", new { content = "First question?" });
        await harness.WaitForMessageTurnRequestAsync();
        var firstHandle = Assert.Single(harness.Launcher.Handles);
        firstHandle.EmitEventWithFields("completed", taskId, new Dictionary<string, object?> { ["text"] = "First answer." });

        await PollAsync.WaitAsync(
            async () => await harness.RecordStore.ReadAsync(taskId) is RemediationTaskRecordParseResult.Parsed p &&
                p.Entries.OfType<RemediationTaskRecordEntry.Message>().Count() == 2,
            TimeSpan.FromSeconds(10),
            "Expected 2 messages on the Remediation Task Record within 10s.");

        await harness.PostJsonAsync($"/api/remediation-tasks/{taskId}/messages", new { content = "Second question?" });
        await harness.WaitForMessageTurnRequestAsync(count: 2);

        var secondRequest = harness.Launcher.MessageTurnRequests[1];
        Assert.Equal("Second question?", secondRequest.Message);
        Assert.Equal(2, secondRequest.PriorMessages.Count);
        Assert.Equal(RemediationTaskRecordFormat.SenderHuman, secondRequest.PriorMessages[0].Sender);
        Assert.Equal("First question?", secondRequest.PriorMessages[0].Text);
        Assert.Equal(RemediationTaskRecordFormat.SenderAgent, secondRequest.PriorMessages[1].Sender);
        Assert.Equal("First answer.", secondRequest.PriorMessages[1].Text);
    }
}

/// <summary>
/// Real-Kestrel harness for the full US5 messaging surface: HTTP endpoints (attach-
/// context/messages/history, plus authorize for the FR-011 execution-reach assertion) and
/// a genuine SignalR client on <c>/hubs/remediation-lifecycle</c> so
/// <c>remediationMessageTurnChanged</c> broadcasts are observable — TestServer's in-memory
/// transport cannot host a real SignalR connection the way this needs, so this mirrors
/// <c>LintRemediationProposalMaterializationTests</c>' real-port pattern instead of
/// <c>RemediationAuthorizationTests</c>' TestServer one.
/// </summary>
internal sealed class RemediationMessagingHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _root;
    private readonly WebApplication _app;

    private RemediationMessagingHarness(
        string root,
        WebApplication app,
        ResolvedGrimoirePaths paths,
        FakeAgentProcessLauncher launcher,
        OperationalStateRepository repository,
        RemediationTaskRecordStore recordStore,
        HttpClient client,
        HubConnection connection)
    {
        _root = root;
        _app = app;
        Paths = paths;
        Launcher = launcher;
        Repository = repository;
        RecordStore = recordStore;
        Client = client;
        Connection = connection;
    }

    public ResolvedGrimoirePaths Paths { get; }
    public FakeAgentProcessLauncher Launcher { get; }
    public OperationalStateRepository Repository { get; }
    public RemediationTaskRecordStore RecordStore { get; }
    public HttpClient Client { get; }
    public HubConnection Connection { get; }

    public static async Task<RemediationMessagingHarness> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-remediation-messaging-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.RemediationTasksDir);

        var launcher = new FakeAgentProcessLauncher(autoPlay: false);
        var repository = new OperationalStateRepository(paths.StateDbPath);
        await repository.InitializeAsync();
        var recordStore = new RemediationTaskRecordStore(paths);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRouting();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton(recordStore);
        builder.Services.AddSingleton<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(launcher);
        builder.Services.AddSingleton<RemediationLifecyclePublisher>(sp => new RemediationLifecyclePublisher(
            sp.GetRequiredService<IHubContext<RemediationLifecycleHub>>(),
            NullLogger<RemediationLifecyclePublisher>.Instance));
        builder.Services.AddSingleton<RemediationRunCoordinator>(sp => new RemediationRunCoordinator(
            repository,
            sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
            sp.GetRequiredService<RemediationLifecyclePublisher>(),
            recordStore,
            paths,
            logger: NullLogger<RemediationRunCoordinator>.Instance));
        builder.Services.AddSingleton<RemediationMessageTurnCoordinator>(sp => new RemediationMessageTurnCoordinator(
            sp.GetRequiredService<Grimoire.Hub.AgentDispatch.IAgentProcessLauncher>(),
            sp.GetRequiredService<RemediationLifecyclePublisher>(),
            recordStore,
            paths,
            logger: NullLogger<RemediationMessageTurnCoordinator>.Instance));

        var app = builder.Build();
        app.MapHub<RemediationLifecycleHub>("/hubs/remediation-lifecycle");
        app.MapGroup("/api/remediation-tasks").MapRemediationTaskEndpoints();
        await app.StartAsync();

        var baseUrl = app.Urls.First();
        var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var connection = new HubConnectionBuilder().WithUrl($"{baseUrl}/hubs/remediation-lifecycle").Build();

        return new RemediationMessagingHarness(root, app, paths, launcher, repository, recordStore, client, connection);
    }

    public Task<HttpResponseMessage> PostJsonAsync(string path, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");
        return Client.PostAsync(path, content);
    }

    public Task InsertTaskAsync(string taskId, string state, string? outcomeReason = null)
        => InsertTaskAsync(taskId, state, DateTimeOffset.UtcNow, outcomeReason);

    public async Task InsertTaskAsync(string taskId, string state, DateTimeOffset now, string? outcomeReason = null)
    {
        await RecordStore.CreateAsync(
            taskId, "2026-08-01-lint-9f8e7d", now, $"Proposal {taskId}", "Agent-authored proposal (verbatim).", null);
        await Repository.InsertRemediationTaskAsync(new RemediationTaskRow(
            TaskId: taskId,
            RunId: "2026-08-01-lint-9f8e7d",
            Title: $"Proposal {taskId}",
            Description: "Agent-authored proposal (verbatim).",
            TargetPath: null,
            State: state,
            ProposedAt: now,
            AuthorizedAt: state is RemediationTaskStates.Authorized or RemediationTaskStates.Executing ? now : null,
            OutcomeReason: outcomeReason,
            UpdatedAt: now));
    }

    public Task WaitForRemediationRequestAsync(int count = 1, int timeoutMs = 10000) =>
        PollAsync.WaitAsync(
            () => Launcher.RemediationRequests.Count >= count,
            TimeSpan.FromMilliseconds(timeoutMs),
            () => $"Expected at least {count} remediation execution requests, saw {Launcher.RemediationRequests.Count}.");

    public Task WaitForMessageTurnRequestAsync(int count = 1, int timeoutMs = 10000) =>
        PollAsync.WaitAsync(
            () => Launcher.MessageTurnRequests.Count >= count,
            TimeSpan.FromMilliseconds(timeoutMs),
            () => $"Expected at least {count} message-turn requests, saw {Launcher.MessageTurnRequests.Count}.");

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();

        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
