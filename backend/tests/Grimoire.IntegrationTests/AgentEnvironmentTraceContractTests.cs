using System.Diagnostics;
using Grimoire.Hub;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Trace;

namespace Grimoire.IntegrationTests;

/// <summary>
/// #61 — the two environment-override events open a correlated span each
/// (<c>signal_type=log</c>), the same idiom every other Hub log event follows. Constitution
/// Principle IV, written from the Feature-003 sampler incident, says a signal is only covered
/// when a test proves it reaches an observer through the <em>production</em> registration: a
/// span emitted under a test-only always-on listener proves the line of code ran and nothing
/// about whether an operator would ever see it.
///
/// <para>
/// So these assertions go through <c>AddHubTelemetry</c> — the real <c>AddSource</c>, the real
/// <c>ParentBased</c> sampler, the real exporter pipeline — with only an in-memory exporter
/// attached to the same builder the Hub uses. The parent is a recorded <c>Grimoire.Hub</c>
/// activity because that is what the spawn path has (<c>hub.ingest_run.trigger</c>); under the
/// production sampler an unsampled parent would drop these spans entirely, which is precisely
/// the failure the principle exists to catch.
/// </para>
/// </summary>
[Collection("HubActivityListenerObservability")]
public class AgentEnvironmentTraceContractTests
{
    private static async Task<(IHost Host, List<Activity> Exported)> StartHostAsync()
    {
        var exported = new List<Activity>();
        var host = new HostBuilder()
            .ConfigureServices(services =>
                services.AddHubTelemetry(tracing => tracing.AddInMemoryExporter(exported)))
            .Build();

        await host.StartAsync();
        return (host, exported);
    }

    [Fact]
    public async Task TheAppliedEvent_IsExportedUnderProductionRegistration_AsAChildOfTheSpawnSpan()
    {
        var (host, exported) = await StartHostAsync();
        try
        {
            using (var parent = HubTracing.ActivitySource.StartActivity("hub.test_parent"))
            {
                Assert.NotNull(parent);
                AgentEnvironmentLogEvents.LogOverrideApplied(
                    NullLogger.Instance,
                    agent: "query",
                    variable: "GRIMOIRE_QUERY_MODEL",
                    source: AgentEnvironmentLogEvents.SecretsFileSource,
                    value: "claude-haiku-4-5");
            }

            await host.StopAsync();

            var span = Assert.Single(exported.Where(a => a.OperationName == "agent.env.override_applied"));
            Assert.NotEqual(default, span.ParentSpanId);
            Assert.True(span.Recorded);
            Assert.Equal("log", span.GetTagItem("signal_type"));
            Assert.Equal("agent.env.override_applied", span.GetTagItem("event_name"));
            Assert.Equal("Information", span.GetTagItem("level"));
            Assert.Equal("query", span.GetTagItem("agent"));
            Assert.Equal("GRIMOIRE_QUERY_MODEL", span.GetTagItem("variable"));
            Assert.Equal("secrets_file", span.GetTagItem("source"));
            Assert.Equal("claude-haiku-4-5", span.GetTagItem("value"));
        }
        finally
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task TheSupersededEvent_IsExportedUnderProductionRegistration_WithBothSources()
    {
        var (host, exported) = await StartHostAsync();
        try
        {
            using (var parent = HubTracing.ActivitySource.StartActivity("hub.test_parent"))
            {
                Assert.NotNull(parent);
                AgentEnvironmentLogEvents.LogOverrideSuperseded(
                    NullLogger.Instance,
                    agent: "lint",
                    variable: "GRIMOIRE_LINT_BASE_URL",
                    supersededSource: AgentEnvironmentLogEvents.ProcessEnvSource,
                    winningSource: AgentEnvironmentLogEvents.SecretsFileSource);
            }

            await host.StopAsync();

            var span = Assert.Single(exported.Where(a => a.OperationName == "agent.env.override_superseded"));
            Assert.NotEqual(default, span.ParentSpanId);
            Assert.True(span.Recorded);
            Assert.Equal("log", span.GetTagItem("signal_type"));
            Assert.Equal("agent.env.override_superseded", span.GetTagItem("event_name"));
            Assert.Equal("Information", span.GetTagItem("level"));
            Assert.Equal("lint", span.GetTagItem("agent"));
            Assert.Equal("GRIMOIRE_LINT_BASE_URL", span.GetTagItem("variable"));
            Assert.Equal("process_env", span.GetTagItem("superseded_source"));
            Assert.Equal("secrets_file", span.GetTagItem("winning_source"));
        }
        finally
        {
            host.Dispose();
        }
    }
}
