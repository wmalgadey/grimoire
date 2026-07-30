# Agent-Artifact Naming Convention

**Status**: Active (established by feature `010-unified-agent-platform`, ADR-013)
**Enforced by**: `backend/tests/Grimoire.ArchTests/AgentArtifactNamingRuleTests.cs` (rule **N1**, Red/Green probed, standard PR pipeline)

## Rule

Every **agent-specific code artifact** — test file/class, evaluation suite, namespace,
per-agent component — carries its owning agent's name (`Ingest`, `Query`, `Lint`) as a
token in its name. **Unprefixed names are reserved for genuinely cross-agent
artifacts.**

For `Grimoire.Hub`, ownership is expressed at namespace level by an explicit
ownership map (below): agent-owned types live in agent-prefixed namespaces; the
shared dispatch namespace (`Grimoire.Hub.AgentDispatch`) keeps only the cross-agent
port surface.

Instruction folders already conform: `data/agents/<agent>/`.

## Rationale

With the third agent (Lint, feature 013) landed, a name must identify its owner
without archaeology: reviewers, rebasers of parallel branches, and the arch rules
themselves rely on ownership legibility. The drift this convention removes: Ingest-only
artifacts created when Ingest was the sole agent stayed unprefixed
(`ReplayEvalTests`) while their Query siblings were prefixed
(`QueryReplayEvalTests`), so absence of a prefix stopped meaning "cross-agent".

## Cross-agent definition

An artifact is **cross-agent** (and stays unprefixed) iff it serves **two or more
agents, or the platform/harness itself**:

- the Agent Platform (`Grimoire.AgentRuntime.*`) and its tests;
- eval capture/replay/workspace/scoring machinery (`Grimoire.EvalRunner.*` outside
  the per-agent scenario catalogs) and its infra tests;
- shared fixtures (`*.Fakes` namespaces) — a fixture shared by ingest and query
  tests stays unprefixed (spec edge case);
- Hub cross-agent namespaces per the ownership map below.

A cross-agent **namespace** may contain per-agent endpoint types of shared
infrastructure (e.g. `IngestLifecycleHub` in `Grimoire.Hub.Realtime`,
`QueuedIngestRun` in `Grimoire.Hub.OperationalState`): there the namespace, not the
type prefix, is the ownership statement. The exception is
`Grimoire.Hub.AgentDispatch`, which is held to the stricter standard of containing
only the shared port surface (`IAgentProcessLauncher`, `AgentRunEvent`,
`Adapters.AgentProcess`) — per-agent dispatch types belong in
`Grimoire.Hub.IngestDispatch` / `Grimoire.Hub.QueryDispatch`.

### Hub namespace-ownership map (mirrored in N1)

| Owner | Namespaces |
|---|---|
| Ingest | `Grimoire.Hub.IngestSubmission`, `Grimoire.Hub.IngestDispatch`, `Grimoire.Hub.IngestTaskArtifact` |
| Query | `Grimoire.Hub.QueryDispatch`, `Grimoire.Hub.QuerySubmission`, `Grimoire.Hub.QueryRunArtifact` |
| Lint | `Grimoire.Hub.LintDispatch`, `Grimoire.Hub.LintFindings` |
| Cross-agent | `Grimoire.Hub` (root), `Grimoire.Hub.AgentDispatch`, `Grimoire.Hub.Realtime`, `Grimoire.Hub.Runtime`, `Grimoire.Hub.ContentRoot`, `Grimoire.Hub.OperationalState`, `Grimoire.Hub.Conversion` |

## Exemption list

The following unprefixed artifacts are classified cross-agent with justification.
This table is **mirrored verbatim in the N1 test fixture**
(`AgentArtifactNamingRuleTests.ExemptedTypeNames`); any drift between this table and
the fixture fails the build.

| Exempted type | Justification |
|---|---|
| `ReplayContractTests` | ADR-012 replay-seam contract tests of the shared eval infrastructure |
| `StalenessTests` | Staleness-fingerprint machinery tests, shared eval infrastructure |
| `CaptureHygieneTests` | Capture-pipeline hygiene tests, shared eval infrastructure |
| `EvalProviderResolverTests` | Eval provider/env resolution, shared eval infrastructure |
| `TimeoutEnforcingModelClientTests` | Shared eval model-client decorator tests |
| `SyntheticRecordings` | Shared synthetic-recording fixture builder for eval infra tests |
| `LocalEnvFileTests` | Shared local-secrets/env-file loader tests |
| `PathTraversalTests` | Guardrail path-canonicalization tests of the shared platform tool boundary |
| `PolicyMisconfigurationTests` | Shared policy-loader fail-closed tests |
| `TraceContextPropagationTests` | Cross-process trace parenting covering both agents' spawn paths |
| `HubRequestTracingTests` | Hub-wide request tracing (cross-agent Hub infrastructure) |
| `QueryConcurrencyIndependenceTests` | Ingest/query interaction test: proves Query dispatch never waits on Ingest's single-slot queue; exercises both pipelines (the query surface via HTTP + shared fakes) |
| `HexagonalPortsAdapterRuleTests` | Solution-wide ADR-010 structural rule; its concrete forbidden-type anchors are incidentally ingest adapters |
| `RuntimePathsBoundaryRuleTests` | Solution-wide ADR-009 structural rule; anchors incidental, boundary is solution-wide |
| `CredentialScopingTests` | Exercises only the shared spawn/credential machinery (`AgentProcessHost.BuildChildEnvironment`, `LocalSecretsLoader`) applied to every agent spawn (ADR-004) |

Also cross-agent by construction (not name-listed): everything in `*.Fakes`
namespaces, `Grimoire.AgentRuntime.*`, `Grimoire.EvalRunner` machinery outside the
scenario catalogs (e.g. `ReplayAdapterTests`, which tests the shared
`ReplayModelClient` adapter at the `IModelClient` port), and the record types
`ScenarioDefinition`/`SampleSpec` (consumed by both agents' scenario catalogs).

## Rename map (feature 010, old → new)

Parallel branches (011/012/013) rebase mechanically against this map. Scenario
ids/slugs, `data/evals/recordings/<scenario>/` directory names, HTTP/SignalR routes,
artifact schemas, and every observability identity are **unchanged** — these renames
touch code identifiers, file names, and namespaces only.

### Test and eval artifacts

| Old | New |
|---|---|
| `Grimoire.AgentEvals/ReplayEvalTests.cs` (`ReplayEvalTests`) | `IngestReplayEvalTests.cs` (`IngestReplayEvalTests`) |
| `Grimoire.ArchTests/GuardedWriteBoundaryRuleTests.cs` | `IngestAgentGuardedWriteBoundaryRuleTests.cs` (class likewise) |
| `ObservabilityLogTests` | `IngestObservabilityLogTests` |
| `ObservabilityMetricsTests` | `IngestObservabilityMetricsTests` |
| `ObservabilityTraceTests` | `IngestObservabilityTraceTests` |
| `AgentRunLifecycleTests` | `IngestRunLifecycleTests` |
| `AgentTaskArtifactTests` | `IngestTaskArtifactTests` |
| `InstructionContextTests` | `IngestInstructionContextTests` |
| `InstructionLoadFailureTests` | `IngestInstructionLoadFailureTests` |
| `UserPromptTests` | `IngestUserPromptTests` |
| `RunQueueTests` | `IngestRunQueueTests` |
| `RunSupervisionTests` | `IngestRunSupervisionTests` |
| `RunActivityRealtimeTests` | `IngestRunActivityRealtimeTests` |
| `FailureAndReconciliationTests` | `IngestFailureAndReconciliationTests` |
| `ConvertStepTests` | `IngestConvertStepTests` |
| `SourceArtifactPersistenceTests` | `IngestSourceArtifactPersistenceTests` |
| `SubmissionPromptApiTests` | `IngestSubmissionPromptApiTests` |
| `TaskRecordApiTests` | `IngestTaskRecordApiTests` |
| `TaskRecordLogEventTests` | `IngestTaskRecordLogEventTests` |
| `TaskRecordMetricsTests` | `IngestTaskRecordMetricsTests` |
| `TaskRecordTraceTests` | `IngestTaskRecordTraceTests` |
| `TaskRecordWatcherTests` | `IngestTaskRecordWatcherTests` |
| `OperationalStateAndDispatchTests` | `IngestOperationalStateAndDispatchTests` |
| `KanbanBoardApiTests` | `IngestKanbanBoardApiTests` |
| `GovernanceIdentityTests` | `IngestGovernanceIdentityTests` |
| `UrlContentFetcherTests` | `IngestUrlContentFetcherTests` |
| `PathConfiguration/DispatchPathArgumentsTests` | `PathConfiguration/IngestDispatchPathArgumentsTests` |
| `PathConfiguration/RepoLessStartupTests` | `PathConfiguration/IngestRepoLessStartupTests` |

### Source artifacts

| Old | New |
|---|---|
| `Grimoire.EvalRunner.Scenarios.ScenarioDefinitions` (static catalog) | `IngestScenarioDefinitions` (file `IngestScenarioDefinitions.cs`; scenario ids unchanged) |
| `ScenarioDefinition`/`SampleSpec` records (same file) | extracted to `Scenarios/ScenarioDefinition.cs`, **unprefixed** (cross-agent) |
| `Grimoire.Hub.AgentDispatch.IngestRunCoordinator` (+ `RunActivitySnapshot`) | `Grimoire.Hub.IngestDispatch.IngestRunCoordinator` (+ `RunActivitySnapshot`) |
| `Grimoire.Hub.AgentDispatch.IngestAgentRequest` | `Grimoire.Hub.IngestDispatch.IngestAgentRequest` |
| `Grimoire.Hub.AgentDispatch.QueryAgentRequest` (+ `QueryPriorTurn`) | `Grimoire.Hub.QueryDispatch.QueryAgentRequest` (+ `QueryPriorTurn`) |
| `Grimoire.Hub.Submission.SubmissionService` | `Grimoire.Hub.IngestSubmission.SubmissionService` |
| `Grimoire.Hub.Submission.SubmitSourceOptions` | `Grimoire.Hub.IngestSubmission.SubmitSourceOptions` |
| `Grimoire.Hub.TaskArtifact.HubTaskArtifactDocument` | `Grimoire.Hub.IngestTaskArtifact.HubTaskArtifactDocument` |
| `Grimoire.Hub.TaskArtifact.HubTaskArtifactWriter` | `Grimoire.Hub.IngestTaskArtifact.HubTaskArtifactWriter` |
| `Grimoire.IngestAgent.AgentCliOptions` | `Grimoire.IngestAgent.IngestCliOptions` |
| `Grimoire.IngestAgent.AgentCore.*` (namespace dissolved) | `Grimoire.IngestAgent.*` (`IngestAgentLoopInstrumentation`, `IngestToolCallInstrumentation`) |
| `Grimoire.IngestAgent/TelemetryBootstrap.cs` | deleted → `Grimoire.AgentRuntime.Telemetry.AgentTelemetryBootstrap` |
| `Grimoire.QueryAgent/QueryAgentTelemetryBootstrap.cs` | deleted → `Grimoire.AgentRuntime.Telemetry.AgentTelemetryBootstrap` |

`IAgentProcessLauncher`, `AgentRunEvent`, and `Adapters/AgentProcess` deliberately
**stay** in cross-agent `Grimoire.Hub.AgentDispatch` (ADR-010/ADR-011 port table and
containment rules keep their anchor).

## Classification decisions (T028–T030, T041)

- `CredentialScopingTests` — **cross-agent, exempt** (see table; evidence: it
  references only `AgentProcessHost`/`LocalSecretsLoader` in the shared dispatch
  adapter namespace; the credential-stripping/injection contract applies to every
  agent spawn).
- `TaskRecord*Tests` — **Ingest-owned, renamed**: Task Records are the ingest
  Kanban read model; the tests' references are confined to
  `Grimoire.Hub.IngestSubmission`.
- `ScenarioDefinition` vs `ScenarioDefinitions` — the `ScenarioDefinition` /
  `SampleSpec` records are consumed by both `IngestScenarioDefinitions` and
  `QueryScenarioDefinitions`: **cross-agent, extracted, unprefixed**. The
  Ingest-only static catalog was renamed.
- `ReplayAdapterTests` — **cross-agent, unprefixed** (tests the shared
  `ReplayModelClient`/`TurnCaptureModelClient` adapters at the `IModelClient` port;
  reference analysis confirms no single-agent ownership).
- `IngestSubmissionPipelineFixture` (in `Fakes/`) — already carries the Ingest
  token; conformant.
