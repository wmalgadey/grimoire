namespace Grimoire.IntegrationTests;

/// <summary>
/// Analysis remediation - groups the observability tests that subscribe process-global
/// <see cref="System.Diagnostics.Metrics.MeterListener"/> / <see cref="System.Diagnostics.ActivityListener"/>
/// instances to the shared <c>Grimoire.IngestAgent</c> meter and activity source and then assert by
/// counting measurements/spans (e.g. <c>Assert.Single(ingest_agent.run)</c>). Because those listeners
/// are process-wide, a concurrently-running test emitting the same signal perturbs the counts
/// non-deterministically. <c>DisableParallelization</c> keeps this collection from running alongside
/// any other test, so the observed signal set is exactly what the test itself produced
/// (Constitution II: harness tests MUST be deterministic and hermetic).
/// </summary>
[CollectionDefinition("IngestAgentObservabilityListeners", DisableParallelization = true)]
public sealed class IngestAgentObservabilityCollection;
