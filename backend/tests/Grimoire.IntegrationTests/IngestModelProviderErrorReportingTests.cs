using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.IngestDispatch;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T050 (023-task-ui-improvements, converge input; US1 / FR-006): when the model provider
/// rejects a request, the operator has to be able to tell <em>why</em>. Before this, every
/// provider rejection reached the card, the detail view, and the status history as a bare
/// "unexpected status 400" — the provider's own explanation, which the response body
/// carries, was discarded by the adapter.
///
/// The provider is stood up as a real local HTTP listener the adapter is pointed at via
/// <c>GRIMOIRE_INGEST_BASE_URL</c> (ADR-004/ADR-012 composition), and the real Ingest agent
/// process runs against it — so what is asserted is Grimoire's own translation of a provider
/// error into recorded failure text, never the SDK's exception formatting (Principle II,
/// "Test what we own").
/// </summary>
public class IngestModelProviderErrorReportingTests
{
    private const string ProviderDetail = "max_tokens: 8096 > 4096, which is the maximum allowed";

    private static string ProviderShapedBody(string message)
        => FakeAnthropicEndpoint.ErrorBody("invalid_request_error", message);

    [Fact]
    public async Task ProviderRejection_RecordsTheProviderMessage_OnEventArtifactAndDetail()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.BadRequest, ProviderShapedBody(ProviderDetail));

        using var run = await RealIngestAgentRun.ExecuteAsync(provider.BaseUrl);

        // 1. The terminal run event the Hub supervises on (ADR-008 event channel).
        Assert.Contains("400", run.FailedEventReason);
        Assert.Contains(ProviderDetail, run.FailedEventReason);

        // 2. The task artifact the board and history read.
        Assert.Contains("400", run.ArtifactFailureReason);
        Assert.Contains(ProviderDetail, run.ArtifactFailureReason);

        // 3. The detail response the operator actually looks at.
        var detailFailureReason = await run.ReadDetailFailureReasonAsync();
        Assert.Contains("400", detailFailureReason);
        Assert.Contains(ProviderDetail, detailFailureReason);

        // Both artifact writers persist only `failure_reason.Split('\n')[0]`, so a
        // multi-line composition would silently lose everything after the first line.
        Assert.DoesNotContain('\n', run.FailedEventReason);
        Assert.DoesNotContain('\r', run.FailedEventReason);
    }

    [Fact]
    public async Task ProviderRejection_WithAnUnparseableBody_StillRecordsTheStatus()
    {
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.BadRequest, "<html><body>Bad Request</body></html>");

        using var run = await RealIngestAgentRun.ExecuteAsync(provider.BaseUrl);

        // A body that is not provider-shaped must degrade to the status alone, never throw
        // its way past the recording path and leave the run without a readable reason.
        Assert.Contains("400", run.ArtifactFailureReason);
        Assert.False(string.IsNullOrWhiteSpace(run.FailedEventReason));
        Assert.Contains("400", run.FailedEventReason);
    }

    [Fact]
    public async Task ProviderMessageContainingACredential_IsRedactedInTheRecordedText()
    {
        // ADR-013: ErrorSanitizer runs over whatever text reaches the recording path. The
        // richer message must not become a new way for a credential echoed by the provider
        // to land in a task artifact.
        const string leaked = "sk-ant-api03-AAAABBBBCCCCDDDD";
        await using var provider = await FakeAnthropicEndpoint.StartAsync(
            HttpStatusCode.BadRequest,
            ProviderShapedBody($"invalid x-api-key: {leaked} was rejected"));

        using var run = await RealIngestAgentRun.ExecuteAsync(provider.BaseUrl);

        Assert.DoesNotContain(leaked, run.ArtifactFailureReason);
        Assert.DoesNotContain(leaked, run.FailedEventReason);
        Assert.Contains("[REDACTED]", run.ArtifactFailureReason);
    }

    /// <summary>
    /// One real Ingest agent child process (ADR-002 spawn model, ADR-022 published worker
    /// DLL) run against the fake provider, with everything the run recorded collected for
    /// state-based assertions.
    /// </summary>
    private sealed class RealIngestAgentRun : IDisposable
    {
        private readonly IngestSubmissionPipelineFixture _fixture;

        private RealIngestAgentRun(
            IngestSubmissionPipelineFixture fixture,
            string taskId,
            string failedEventReason,
            string artifactFailureReason)
        {
            _fixture = fixture;
            TaskId = taskId;
            FailedEventReason = failedEventReason;
            ArtifactFailureReason = artifactFailureReason;
        }

        public string TaskId { get; }
        public string FailedEventReason { get; }
        public string ArtifactFailureReason { get; }

        public static async Task<RealIngestAgentRun> ExecuteAsync(string providerBaseUrl)
        {
            var fixture = new IngestSubmissionPipelineFixture();
            try
            {
                var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
                var instructionsDir = Path.Combine(
                    repoRoot, "backend", "src", "Grimoire.IngestAgent", "Instructions");
                var agentWorkerPath = Path.Combine(
                    repoRoot, ".grimoire", "agents", "ingest", "Grimoire.IngestAgent.dll");

                // ADR-004: the child's credentials and model endpoint come from the secrets
                // file, never the parent environment — so pointing the run at the fake
                // provider is a .env line, exactly as an operator would redirect it.
                var envPath = Path.Combine(fixture.Root, ".env");
                await File.WriteAllTextAsync(envPath,
                    "ANTHROPIC_AUTH_TOKEN=sk-ant-api03-testtoken\n" +
                    "GRIMOIRE_INGEST_MODEL=fake-model\n" +
                    $"GRIMOIRE_INGEST_BASE_URL={providerBaseUrl}\n");

                var sourcePath = Path.Combine(fixture.Root, "source.md");
                await File.WriteAllTextAsync(sourcePath, "# Provider Error Fixture\n\nBody text.\n");

                var taskId = $"2026-08-16-ingest-{Guid.NewGuid():N}";
                var processHost = new AgentProcessHost(
                    new LocalSecretsLoader(envPath),
                    agentWorkerPath,
                    Path.Combine(repoRoot, ".grimoire", "agents", "query", "Grimoire.QueryAgent.dll"),
                    Path.Combine(repoRoot, ".grimoire", "agents", "lint", "Grimoire.LintAgent.dll"));

                var request = new IngestAgentRequest(
                    TaskId: taskId,
                    SourceRef: sourcePath,
                    SourceKind: "file",
                    WikiRoot: fixture.ContentPaths.Root,
                    ContentRoot: fixture.ContentPaths.Root,
                    TasksDir: fixture.ContentPaths.TasksDir,
                    IndexPath: fixture.ContentPaths.IndexPath,
                    LogPath: fixture.ContentPaths.LogPath,
                    PastedText: null,
                    FoundationPromptPath: Path.Combine(repoRoot, "backend", "src", "Grimoire.AgentRuntime", "Instructions", "foundation-prompt.md"),
                    SystemPromptPath: Path.Combine(instructionsDir, "system-prompt.md"),
                    DefaultUserPromptPath: Path.Combine(instructionsDir, "default-user-prompt.md"),
                    PolicyPath: Path.Combine(instructionsDir, "policy.json"),
                    WriteLocksDir: fixture.ContentPaths.WriteLocksDir,
                    Title: "Provider Error Fixture");

                await using var handle = await processHost.StartAsync(request);

                string? failedReason = null;
                await foreach (var line in handle.ReadStdoutLinesAsync(CancellationToken.None))
                {
                    using var document = JsonDocument.Parse(line);
                    if (document.RootElement.TryGetProperty("type", out var type) &&
                        type.GetString() == "failed")
                    {
                        failedReason = document.RootElement.TryGetProperty("reason", out var reason)
                            ? reason.GetString()
                            : null;
                        break;
                    }
                }

                Assert.False(string.IsNullOrWhiteSpace(failedReason),
                    "The agent run did not emit a `failed` event with a reason.");

                var artifact = await File.ReadAllTextAsync(fixture.TaskArtifactPathFor(taskId));
                var frontmatter = Grimoire.Hub.IngestSubmission.IngestTaskArtifactFrontmatter.TryParse(artifact);
                Assert.NotNull(frontmatter);
                Assert.Equal("failed", frontmatter!.Status);

                return new RealIngestAgentRun(fixture, taskId, failedReason!, frontmatter.FailureReason ?? string.Empty);
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public async Task<string> ReadDetailFailureReasonAsync()
        {
            using var host = await IngestApiHost.BuildAsync(_fixture);
            var detail = await host.GetTestClient()
                .GetFromJsonAsync<JsonElement>($"/api/ingest-submissions/{TaskId}");
            return detail.GetProperty("failureReason").GetString() ?? string.Empty;
        }

        public void Dispose() => _fixture.Dispose();

        private static string FindRepoRoot(string start)
        {
            var current = Path.GetFullPath(start);
            while (true)
            {
                if (Directory.Exists(Path.Combine(current, ".specify")) &&
                    Directory.Exists(Path.Combine(current, "specs")))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName
                    ?? throw new InvalidOperationException("Could not find repository root.");
            }
        }
    }
}
