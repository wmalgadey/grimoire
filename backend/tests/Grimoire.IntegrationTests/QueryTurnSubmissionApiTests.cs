using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.QuerySubmission;
using Grimoire.Hub.Realtime;
using Grimoire.Hub.Runtime.Paths;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T027 (US1) — HTTP contract of Query Turn submission
/// (contracts/query-conversation-api.md): 202 Accepted with turnId/position/state for a
/// valid prompt, 400 for empty/whitespace/over-max-length prompt with no turn created.
/// </summary>
public class QueryTurnSubmissionApiTests
{
    [Fact]
    public async Task PostTurn_ValidPrompt_Returns202_WithTurnIdPositionAndState()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await BuildHostAsync(launcher, root: CreateTempRoot());
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-1/turns",
            new { prompt = "What does the wiki say about ADR-004?" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("turnId").GetString()));
        Assert.Equal(1, json.GetProperty("position").GetInt32());
        Assert.Equal("running", json.GetProperty("state").GetString());

        Assert.Single(launcher.QueryRequests);
        Assert.Equal("c-1", launcher.QueryRequests[0].ConversationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostTurn_EmptyOrWhitespacePrompt_Returns400_NoTurnCreated(string prompt)
    {
        var launcher = new FakeAgentProcessLauncher();
        using var host = await BuildHostAsync(launcher, root: CreateTempRoot());
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/api/query-conversations/c-2/turns", new { prompt });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(launcher.QueryRequests);
    }

    [Fact]
    public async Task PostTurn_PromptOverMaxLength_Returns400_NoTurnCreated()
    {
        var launcher = new FakeAgentProcessLauncher();
        using var host = await BuildHostAsync(launcher, root: CreateTempRoot());
        var client = host.GetTestClient();

        var overLong = new string('a', QuerySubmissionValidator.PromptMaxLength + 1);
        var response = await client.PostAsJsonAsync("/api/query-conversations/c-3/turns", new { prompt = overLong });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(launcher.QueryRequests);
    }

    // T013 (011-query-conversations, contracts/query-conversation-api.md): the body
    // contains only `prompt`; `position` is Hub-assigned = recorded turns + 1.
    [Fact]
    public async Task PostTurn_FollowUpWithPromptOnlyBody_GetsHubAssignedPosition()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("First answer.", TimeSpan.Zero)],
        };
        var root = CreateTempRoot();
        using var host = await BuildHostAsync(launcher, root);
        var client = host.GetTestClient();

        var firstTurnId = await QueryConversationRecordLifecycleTests.SubmitAsync(client, "c-position", "First question?");
        await QueryConversationRecordLifecycleTests.WaitForStateAsync(client, firstTurnId, "completed");
        // 019-fast-test-tier (ADR-021 edge case, genuine race surfaced by suite
        // parallelization): the turn's in-memory status flips before
        // ConversationRecordStore.AppendTurnAsync's cache update completes — the very next
        // submission's Hub-assigned position reads that cache, so wait for the append to
        // actually land before submitting the follow-up (mirrors RunScriptedTurnAsync's fix).
        var recordPath = BuildResolvedPaths(root).ConversationRecordPathFor("c-position");
        await PollAsync.WaitAsync(
            () => File.Exists(recordPath) && Grimoire.Hub.QueryConversations.ConversationRecordFormat.Parse(File.ReadAllText(recordPath))
                is Grimoire.Hub.QueryConversations.ConversationRecordParseResult.Parsed { Turns.Count: >= 1 },
            TimeSpan.FromSeconds(10),
            $"Expected the Conversation Record at '{recordPath}' to contain the first turn within 10s.");

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-position/turns", new { prompt = "Second question?" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("position").GetInt32());
    }

    // T013: conversationId names the record file, so path safety is enforced at the
    // boundary — violations of ^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$ are rejected 400 with
    // no turn created (path-traversal fixtures included).
    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("-leading-dash")]
    [InlineData("_leading-underscore")]
    public async Task PostTurn_InvalidConversationId_Returns400_NoTurnCreated(string conversationId)
    {
        var launcher = new FakeAgentProcessLauncher();
        using var host = await BuildHostAsync(launcher, root: CreateTempRoot());
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            $"/api/query-conversations/{Uri.EscapeDataString(conversationId)}/turns",
            new { prompt = "Valid prompt?" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(launcher.QueryRequests);
    }

    [Fact]
    public async Task PostTurn_ConversationIdOver64Chars_Returns400_NoTurnCreated()
    {
        var launcher = new FakeAgentProcessLauncher();
        using var host = await BuildHostAsync(launcher, root: CreateTempRoot());
        var client = host.GetTestClient();

        var overLong = new string('a', 65);
        var response = await client.PostAsJsonAsync(
            $"/api/query-conversations/{overLong}/turns", new { prompt = "Valid prompt?" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(launcher.QueryRequests);
    }

    // T013: a stale client still sending priorTurns is accepted; the extra field is
    // ignored by JSON binding and the record stays authoritative (FR-006).
    [Fact]
    public async Task PostTurn_StaleClientStillSendingPriorTurns_IsAccepted_AndTheFieldIsIgnored()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true, simulatedRunDuration: TimeSpan.FromSeconds(5));
        using var host = await BuildHostAsync(launcher, root: CreateTempRoot());
        var client = host.GetTestClient();

        var stalePriorTurns = new object[]
        {
            new { position = 1, prompt = "Fabricated?", answer = "Fabricated answer.", state = "completed" },
            new { position = 2, prompt = "Also fabricated?", answer = "Another one.", state = "completed" },
        };

        var response = await client.PostAsJsonAsync(
            "/api/query-conversations/c-stale/turns",
            new { prompt = "Real question?", priorTurns = stalePriorTurns });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The record (empty — nothing recorded yet) is authoritative, not the stale payload.
        Assert.Equal(1, json.GetProperty("position").GetInt32());
        var request = Assert.Single(launcher.QueryRequests);
        Assert.Empty(request.PriorTurns);
    }

    [Fact]
    public async Task GetTurn_UnknownTurnId_Returns404()
    {
        using var host = await BuildHostAsync(new FakeAgentProcessLauncher(), root: CreateTempRoot());
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/query-turns/never-submitted");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    internal static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-query-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    internal static ResolvedGrimoirePaths BuildResolvedPaths(string root) => new(
        BaseDir: root,
        DataDir: root,
        ContentRoot: Path.Combine(root, "wiki"),
        TasksDir: Path.Combine(root, "wiki", "tasks"),
        IndexPath: Path.Combine(root, "wiki", "index.md"),
        LogPath: Path.Combine(root, "wiki", "log.md"),
        RawOriginalsDir: Path.Combine(root, "raw", "originals"),
        RawSourcesDir: Path.Combine(root, "raw", "sources"),
        StateDbPath: Path.Combine(root, "state.db"),
        SecretsFilePath: Path.Combine(root, ".env"),
        InstructionsDir: Path.Combine(root, "agents", "ingest"),
        SystemPromptPath: Path.Combine(root, "agents", "ingest", "system-prompt.md"),
        DefaultUserPromptPath: Path.Combine(root, "agents", "ingest", "default-user-prompt.md"),
        PolicyPath: Path.Combine(root, "agents", "ingest", "policy.json"),
        AgentWorkerPath: "unused",
        QueryInstructionsDir: Path.Combine(root, "agents", "query"),
        QuerySystemPromptPath: Path.Combine(root, "agents", "query", "system-prompt.md"),
        QueryPolicyPath: Path.Combine(root, "agents", "query", "policy.json"),
        ConversationsDir: Path.Combine(root, "conversations"),
        QueryAgentWorkerPath: "unused",
        WriteLocksDir: Path.Combine(root, "write-locks"),
        FindingsDir: Path.Combine(root, "findings"),
        LintInstructionsDir: Path.Combine(root, "agents", "lint"),
        LintSystemPromptPath: Path.Combine(root, "agents", "lint", "system-prompt.md"),
        LintPolicyPath: Path.Combine(root, "agents", "lint", "policy.json"),
        LintAgentWorkerPath: "unused",
        RemediationTasksDir: Path.Combine(root, "remediation-tasks"),
        LintPidPath: Path.Combine(root, "lint.pid"),
        Locations: []);

    internal static async Task<IHost> BuildHostAsync(
        FakeAgentProcessLauncher launcher,
        string root,
        int concurrencyLimit = 3,
        TimeSpan? livenessWindow = null,
        ConversationRecordStore? recordStore = null,
        ILogger<QueryRunCoordinator>? coordinatorLogger = null)
    {
        var resolvedPaths = BuildResolvedPaths(root);

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSignalR();
                    services.AddSingleton<IAgentProcessLauncher>(launcher);
                    services.AddSingleton(resolvedPaths);
                    services.AddSingleton(new QueryConcurrencyOptions { QueryConcurrencyLimit = concurrencyLimit });
                    services.AddSingleton<ConversationRecordStore>(sp =>
                        recordStore ?? new ConversationRecordStore(resolvedPaths));
                    services.AddSingleton<QuerySubmissionValidator>();
                    services.AddSingleton<QueryLifecyclePublisher>(sp => new QueryLifecyclePublisher(
                        sp.GetRequiredService<IHubContext<QueryLifecycleHub>>(), NullLogger<QueryLifecyclePublisher>.Instance));
                    services.AddSingleton<QueryRunCoordinator>(sp => new QueryRunCoordinator(
                        sp.GetRequiredService<IAgentProcessLauncher>(),
                        sp.GetRequiredService<QueryLifecyclePublisher>(),
                        sp.GetRequiredService<ConversationRecordStore>(),
                        resolvedPaths,
                        sp.GetRequiredService<QueryConcurrencyOptions>(),
                        livenessWindow: livenessWindow,
                        logger: coordinatorLogger ?? NullLogger<QueryRunCoordinator>.Instance));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<QueryLifecycleHub>("/hubs/query-lifecycle");
                        endpoints.MapGroup("/api/query-conversations").MapQueryConversationEndpoints();
                        endpoints.MapGroup("/api/query-turns").MapQueryTurnEndpoints();
                    });
                });
            });

        return await hostBuilder.StartAsync();
    }
}
