namespace Grimoire.IntegrationTests;

/// <summary>
/// 019-fast-test-tier (US2, FR-011/SC-003 flip of <c>parallelizeTestCollections</c>) — a
/// genuine race surfaced by parallelization, not papered over by reverting it (per the
/// spec's own edge case): <see cref="System.Diagnostics.ActivitySource.AddActivityListener"/>
/// registers a listener process-wide, with no per-test scoping. Any two of these tests
/// running concurrently can cross-contaminate each other in two ways: (1) a test using
/// <c>AddHubTelemetry</c> (which wires OpenTelemetry's ASP.NET Core instrumentation onto
/// the process-wide "Microsoft.AspNetCore.Hosting" activity source) causes a *different*,
/// concurrently-running test's own HTTP request — even served by a separate in-memory
/// <c>TestServer</c> — to suddenly get a recorded ambient hosting <c>Activity</c>, wrongly
/// parenting spans that assert they are root; (2) two listeners racing to enqueue/observe
/// the same "Grimoire.Hub" activity source under heavy concurrent CPU load produced an
/// observed span-collection miss in practice. <c>DisableParallelization</c> keeps every
/// test that registers a raw process-wide <see cref="System.Diagnostics.ActivityListener"/>
/// from running alongside any other member of this collection, mirroring
/// <see cref="IngestAgentObservabilityCollection"/>'s existing rationale for the
/// <c>Grimoire.IngestAgent</c> meter/activity source — this collection covers the same
/// process-wide-listener hazard for <c>Grimoire.Hub</c> spans and ASP.NET Core hosting
/// instrumentation. <c>IngestAgentObservabilityListeners</c> remains untouched and
/// independent; a class needs at most one of the two collections.
///
/// <para>
/// The same hazard applies to a process-wide
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>, and it bit in CI on 2026-08-21:
/// <c>QueryWriteConflictObservabilityTests</c> listens to the <c>Grimoire.QueryAgent</c>
/// meter's <c>wiki.write_conflict.rejections_total</c> counter and asserted
/// <c>Assert.Single</c> over every measurement it saw, while
/// <c>CatalogEntryFormatEnforcementTests</c> — already in this collection — emitted
/// <c>catalog_entry_malformed</c> on that very counter from a parallel collection. The
/// listener saw two measurements and the run went red on a PR that touched neither test.
/// Any class registering a raw listener on that shared meter belongs here too.
/// </para>
/// </summary>
[CollectionDefinition("HubActivityListenerObservability", DisableParallelization = true)]
public sealed class HubActivityListenerObservabilityCollection;
