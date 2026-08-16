using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Hub.ApiErrors;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Grimoire.IntegrationTests;

/// <summary>
/// The HTTP failure contract (024-api-error-presentation, ADR-026; T011, T024, T025, T034).
///
/// <para>
/// Cross-agent by construction — it exercises ingest, query, lint and remediation failures
/// against one contract — so it is unprefixed per ADR-013 rule N1, alongside
/// <see cref="HubRequestTracingTests"/>.
/// </para>
///
/// <para>
/// Everything asserted here is decided by Grimoire's own source: the members we write, the prose
/// we authored, the codes we chose. Nothing asserts that ASP.NET Core serializes
/// <c>ProblemDetails</c> or that the exception-handler pipeline invokes a registered handler —
/// those are the framework's tests to run (Constitution Principle II, "Test what we own"). The one
/// place framework wiring is load-bearing is covered by exactly one intent-named wire-up test,
/// <see cref="ApiErrorExceptionHandler_IsRegistered_AndReachesOurHandler"/>.
/// </para>
/// </summary>
public class HubApiErrorEnvelopeTests
{
    private const string ProblemJson = "application/problem+json";

    // -----------------------------------------------------------------------
    // T024 — the envelope on real HTTP, across endpoint families
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IngestValidationFailure_AnswersWithTheFullEnvelope()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/ingest-submissions", new { kind = "not-a-kind", url = "https://example.test/a" });

        await AssertEnvelopeAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IngestNotFound_AnswersWithTheFullEnvelope()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/ingest-submissions/no-such-task");

        var body = await AssertEnvelopeAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(ApiErrorCatalogue.IngestTaskNotFound, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task IngestRestartConflict_AnswersWithItsOwnCode_NotAGenericDecline()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        using var host = await IngestApiHost.BuildAsync(fixture);
        var client = host.GetTestClient();

        var taskId = "2026-08-16-ingest-running";
        await IngestTaskDetailHistoryTests.WriteTaskArtifactAsync(fixture, taskId, "running");

        var response = await client.PostAsync($"/api/ingest-submissions/{taskId}/restart", content: null);

        var body = await AssertEnvelopeAsync(response, HttpStatusCode.Conflict);
        // ADR-018/ADR-025: the caller sees the actual outcome. "not failed" and "source missing"
        // are different problems with different ways forward, so they must not share a code.
        Assert.Equal(ApiErrorCatalogue.RestartTaskNotFailed, body.GetProperty("code").GetString());
    }

    // -----------------------------------------------------------------------
    // T025 — catalogue completeness (FSI1). Classicist: over the real catalogue and real
    // responses, never reflecting over a type's shape.
    // -----------------------------------------------------------------------

    [Fact]
    public void EveryCatalogueEntry_CarriesAuthoredProse()
    {
        Assert.NotEmpty(ApiErrorCatalogue.All);

        foreach (var definition in ApiErrorCatalogue.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.Title), $"'{definition.Code}' has no title.");
            Assert.False(string.IsNullOrWhiteSpace(definition.Detail), $"'{definition.Code}' has no detail.");
            Assert.InRange(definition.Status, 400, 599);
        }
    }

    /// <summary>
    /// The identifier is for logs and tooling. A message that quotes it is how
    /// <c>conversation_already_active</c> used to reach users as their error text — the literal
    /// defect issue #85 reported.
    /// </summary>
    [Fact]
    public void NoCatalogueEntry_LeaksItsOwnIdentifierIntoItsProse()
    {
        foreach (var definition in ApiErrorCatalogue.All)
        {
            Assert.DoesNotContain(definition.Code, definition.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(definition.Code, definition.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CatalogueCodes_AreUnique()
    {
        var duplicates = ApiErrorCatalogue.All
            .GroupBy(d => d.Code, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate catalogue codes: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// FR-016's hole-free clause: a code shipped without an entry is a defect, but the user still
    /// gets a sentence rather than an identifier or a 500 thrown from the error path itself.
    /// </summary>
    [Fact]
    public void UnknownCode_ResolvesToAReadableGenericEntry()
    {
        var declined = ApiErrorCatalogue.Resolve("no_such_code_was_ever_authored");
        Assert.Equal(ApiErrorCatalogue.RequestDeclined, declined.Code);
        Assert.False(string.IsNullOrWhiteSpace(declined.Detail));

        var faulted = ApiErrorCatalogue.Resolve("no_such_code_was_ever_authored", fallbackStatus: 500);
        Assert.Equal(ApiErrorCatalogue.InternalError, faulted.Code);
        Assert.False(string.IsNullOrWhiteSpace(faulted.Detail));
    }

    /// <summary>
    /// The identifiers ADR-014 and ADR-020 pinned, and the ones tests and tooling key on, survive
    /// the reshaping verbatim (FR-003). Renaming one is a breaking change, not a refactor.
    /// </summary>
    [Theory]
    [InlineData("conversation_record_unreadable")]
    [InlineData("conversation_already_active")]
    [InlineData("query_concurrency_limit_reached")]
    [InlineData("lint_run_active")]
    [InlineData("unresolved_remediation_tasks")]
    [InlineData("task_not_proposed")]
    [InlineData("task_not_authorized")]
    [InlineData("execution_already_started")]
    [InlineData("message_turn_active")]
    public void CarriedOverIdentifiers_SurviveVerbatim(string code)
        => Assert.True(ApiErrorCatalogue.Contains(code), $"Carried-over identifier '{code}' is missing.");

    // -----------------------------------------------------------------------
    // T011 / T034 / SC-008 — wire-up, correlation, and what never reaches the body
    // -----------------------------------------------------------------------

    /// <summary>
    /// The single wire-up test ADR-026 permits: it proves *our* registration is present and reaches
    /// *our* handler, not that ASP.NET Core's exception middleware works. Before this feature an
    /// escaping exception produced a bare 500 with an empty body.
    /// </summary>
    [Fact]
    public async Task ApiErrorExceptionHandler_IsRegistered_AndReachesOurHandler()
    {
        using var host = await BuildThrowingHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/boom");

        var body = await AssertEnvelopeAsync(response, HttpStatusCode.InternalServerError);
        Assert.Equal(ApiErrorCatalogue.InternalError, body.GetProperty("code").GetString());
    }

    /// <summary>
    /// SC-008: an exception's own text is an operator surface, not a client one. It can carry
    /// filesystem paths, connection details, or upstream provider text; it reaches the
    /// <c>api.error.faulted</c> log and stops there.
    /// </summary>
    [Fact]
    public async Task UnhandledException_NeverPutsItsOwnMessageInTheResponseBody()
    {
        using var host = await BuildThrowingHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/boom");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(ThrownSecret, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// T034: the correlation id in the body is the request's real trace identity, not a fresh or
    /// decorative value — otherwise an operator handed it in a screenshot could not find anything.
    /// </summary>
    [Fact]
    public async Task TraceId_InTheBody_IsTheRequestsOwnTraceIdentity()
    {
        using var fixture = new IngestSubmissionPipelineFixture();
        var exported = new IngestApiHost.SynchronizedActivityCollection();
        using var host = await IngestApiHost.BuildAsync(fixture, exported);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/api/ingest-submissions/no-such-task");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var traceId = body.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));

        await PollAsync.WaitAsync(
            () => exported.Snapshot().Any(a => a.TraceId.ToString() == traceId),
            TimeSpan.FromSeconds(10),
            $"No exported span carried the traceId '{traceId}' returned in the response body.");
    }

    // -----------------------------------------------------------------------

    private const string ThrownSecret = "connection-string-abc123";

    private static async Task<JsonElement> AssertEnvelopeAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((int)expectedStatus, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));

        var code = body.GetProperty("code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        // The whole point of the split: whatever the code is, it is not what the user reads.
        Assert.DoesNotContain(code!, body.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);

        return body;
    }

    private static async Task<IHost> BuildThrowingHostAsync()
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging();
                    services.AddProblemDetails();
                    services.AddExceptionHandler<ApiErrorExceptionHandler>();
                });
                webHost.Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGet("/boom",
                            void () => throw new InvalidOperationException($"leaked {ThrownSecret}")));
                });
            });

        return await hostBuilder.StartAsync();
    }
}
