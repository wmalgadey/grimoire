using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.QueryDispatch;
using Grimoire.Hub.QueryRunArtifact;
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
        PagesDir: Path.Combine(root, "wiki", "pages"),
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
        QueryRunsDir: Path.Combine(root, "query-runs"),
        QueryAgentWorkerPath: "unused",
        Locations: []);

    internal static async Task<IHost> BuildHostAsync(
        FakeAgentProcessLauncher launcher, string root, int concurrencyLimit = 3, TimeSpan? livenessWindow = null)
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
                    services.AddSingleton<QueryRunArtifactWriter>();
                    services.AddSingleton<QuerySubmissionValidator>();
                    services.AddSingleton<QueryLifecyclePublisher>(sp => new QueryLifecyclePublisher(
                        sp.GetRequiredService<IHubContext<QueryLifecycleHub>>(), NullLogger<QueryLifecyclePublisher>.Instance));
                    services.AddSingleton<QueryRunCoordinator>(sp => new QueryRunCoordinator(
                        sp.GetRequiredService<IAgentProcessLauncher>(),
                        sp.GetRequiredService<QueryLifecyclePublisher>(),
                        sp.GetRequiredService<QueryRunArtifactWriter>(),
                        resolvedPaths,
                        sp.GetRequiredService<QueryConcurrencyOptions>(),
                        livenessWindow: livenessWindow,
                        logger: NullLogger<QueryRunCoordinator>.Instance));
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
