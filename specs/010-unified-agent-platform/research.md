# Research: Unified Agent Platform & Naming Convention

**Feature**: `010-unified-agent-platform` | **Date**: 2026-07-27 | **Spec**: `specs/010-unified-agent-platform/spec.md`

All findings below were established by direct survey of the codebase on branch
`010-unified-agent-platform` (state: post-feature-009 merge) and by reading ADR-001
through ADR-012. No NEEDS CLARIFICATION markers remain.

## R1 — Packaging: shared platform library under thin per-agent hosts (not a single parameterized host)

**Decision**: "One platform" is realized as **one shared library
(`Grimoire.AgentRuntime`) extended to own every shared concern, consumed by thin
per-agent host executables** (`Grimoire.IngestAgent`, `Grimoire.QueryAgent`, later
`Grimoire.LintAgent`). Each host shrinks to: an Agent Profile declaration (identity,
telemetry identities, tool registry, per-agent env-var names), its CLI option shape,
and its intent-specific artifact handling. A single parameterized host process
(`--agent ingest|query`) is **rejected for now, deferred not forbidden** — full
rationale and supersession statement in **ADR-013** (drafted with this plan, status
`proposed`).

**Rationale** (condensed; ADR-013 is authoritative):

1. **Behavior preservation is absolute (FR-008/FR-009, SC-003).** Separate host
   processes keep the per-agent OTel service resources (`Grimoire.IngestAgent`,
   `Grimoire.QueryAgent`), the ADR-002/ADR-011 spawn contracts, the Hub's worker-path
   configuration (ADR-009), and every existing structural test — including
   `QueryAgentGuardedWriteBoundaryRuleTests` — valid without reformulation. A single
   host would force rewriting the no-write structural rule and the spawn/launch
   configuration in the same change set that is supposed to be a pure restructuring.
2. **The duplication is library-shaped, not process-shaped.** The measured drift
   (telemetry bootstrap 68 vs 63 near-identical lines, tracing scaffold 28 vs 27,
   model-client composition ~30 lines twice, error sanitization and CLI parsing
   duplicated) all lives in host scaffold code that a library can own. The genuinely
   per-agent remainder (Ingest's task-artifact lifecycle/rollback/log appending;
   Query's stdin conversation scaffold) would still exist as per-agent modules inside
   a single host — merging processes reduces zero duplicated lines beyond what the
   library extraction already removes.
3. **FR-004 and the feature-012 horizon.** ADR-011's premise — Query's no-write
   guarantee is structural because no write tool is compiled into its process — is
   scheduled to fall when feature 012 gives Query guarded write capability. ADR-013
   therefore re-grounds the guarantee as: *an agent's effective capabilities are
   exactly its profile's declared tool registry, enforced at the guarded tool boundary;
   for as long as the Query profile declares no write tool, the per-host structural
   rule (no filesystem-write reachability from the Query host's tool-dispatch path)
   remains in force with its Red/Green probe.* Keeping separate host assemblies keeps
   that rule cheap and exact today, and feature 012 renegotiates only the Query
   profile/policy/rule — not the platform packaging.

**Alternatives considered**:

- **(b) Single parameterized agent host with per-agent profiles**: rejected for this
  feature — it invalidates ADR-011's structural no-write rule ahead of feature 012,
  merges two frozen OTel service identities into one binary, changes the Hub's spawn
  configuration, and saves no per-agent code beyond the library extraction (per-intent
  modules persist). Recorded in ADR-013 as the natural follow-up **if** post-012/013
  hosts degenerate into pure profile declarations.
- **Status quo (two hosts with private scaffolds)**: rejected — it is the drift this
  feature exists to remove (FR-001).

## R2 — Duplication inventory and target consolidation

Survey of `backend/src/Grimoire.IngestAgent` and `backend/src/Grimoire.QueryAgent`
found these shared concerns implemented twice (violating FR-001):

| Concern | Ingest artifact | Query artifact | Consolidation target |
|---|---|---|---|
| Telemetry bootstrap (OTel tracer/meter/logger providers, OTLP export) | `TelemetryBootstrap.cs` (68 lines) | `QueryAgentTelemetryBootstrap.cs` (63 lines) | `Grimoire.AgentRuntime.Telemetry.AgentTelemetryBootstrap` parameterized by service/source/meter name (identities frozen: `Grimoire.IngestAgent`, `Grimoire.QueryAgent`) |
| Tracing scaffold (ActivitySource holder, run-span start) | `IngestAgentTracing.cs` (28 lines) | `QueryAgentTracing.cs` (27 lines) | `Grimoire.AgentRuntime.Telemetry.AgentTracing` parameterized by source name, run-span name, and id attribute (`task_id` / `turn_id`) — span names unchanged |
| Model-client composition (replay/capture/live selection per ADR-012) | `Program.cs::CreateModelClient` | `Program.cs::CreateModelClient` | `Grimoire.AgentRuntime.Composition.ModelClientFactory`, invoked from each host's composition root (ADR-012 env-var contract unchanged; per-agent model/base-url env-var names remain profile inputs) |
| Credential-bearing error sanitization | `Program.cs::SanitizeErrorText` | `Program.cs::SanitizeErrorText` | `Grimoire.AgentRuntime.Composition.ErrorSanitizer` (one implementation) |
| CLI argument parsing scaffold (`--key value` loop, required/optional helpers, heartbeat default) | `Program.cs::ParseArgs` | `Program.cs::ParseArgs` | `Grimoire.AgentRuntime.Composition.AgentArgumentReader`; each host keeps only its own option record (`IngestCliOptions`, `QueryCliOptions`) |
| Startup sequencing (instructions/policy fail-closed load → `started` → heartbeat) | inline in `Program.cs` | inline in `Program.cs` (comment: "mirrors Ingest's sequencing") | `Grimoire.AgentRuntime.Host.AgentHost` template with intent hooks (see R3); already-shared loaders (`SystemPromptLoader`, `PolicyLoader`) unchanged |

Already correctly consolidated (feature 008/009, no action): `AgentLoop`,
`IModelClient` + Anthropic/Replay adapters, `GuardedToolExecutor`/`ToolRegistry`/
`WriteJournal`, `RunEventEmitter`, `SystemPromptLoader`/`PolicyLoader`.

Genuinely per-agent (stays in hosts, per FR-002): Ingest's `TaskArtifact/`,
`IngestLog/`, `Source/`, rollback + all-denied failure handling, user-prompt
resolution; Query's stdin conversation input + harness message scaffold; each agent's
instrumentation adapters (`IngestAgentInstrumentation`, `QueryAgentInstrumentation`),
metrics/log-event definitions (frozen identities), tool registry, and profile.

## R3 — Agent Profile: shape and consumption

**Decision**: The Agent Profile from the spec's Terminology is realized as a code-side
declaration record in each host assembly (not a config file):

- **Identity**: agent name (`ingest`, `query`) + telemetry identities (OTel service
  name, ActivitySource/Meter name, run-span name, correlation attribute name).
- **Tool set**: the agent's `ToolRegistry` instance (`IngestToolRegistry`:
  `list_files`, `read_file`, `write_file`; `QueryToolRegistry`: `list_files`,
  `read_file` — unchanged).
- **Instruction surface**: paths arrive per spawn via CLI (ADR-007/ADR-009 unchanged);
  the profile declares which instruction documents the agent requires
  (system prompt + default user prompt for Ingest; system prompt only for Query).
- **Policy**: per-agent `policy.json` path (Hub-supplied, ADR-006 unchanged).
- **Model configuration env-var names** (ADR-004): `ANTHROPIC_*`/default for Ingest,
  `GRIMOIRE_QUERY_MODEL`/`GRIMOIRE_QUERY_BASE_URL` for Query — profile inputs to the
  shared `ModelClientFactory`.

The platform's `AgentHost` consumes a profile plus an intent handler (the per-agent
artifact/conversation logic) — behavior differences remain profile/hook-expressed, no
agent-conditional branches inside the platform (FR-002). The profile is a plain record
consumed in-process; no new port is needed (no new external system, Principle I).

## R4 — Naming convention: scope, document, and enforcement mechanism

**Decision**:

- **Convention document**: `docs/conventions/agent-artifact-naming.md`, versioned in
  git, stating (a) the rule — every agent-specific code artifact (test files/classes,
  evaluation suites, namespaces, per-agent components, instruction folders) carries its
  agent's name; unprefixed names are reserved for genuinely cross-agent artifacts —
  (b) the rationale, (c) the explicit cross-agent definition (serves ≥2 agents or the
  platform/harness itself ⇒ unprefixed, per the spec's edge case), and (d) the full
  old→new rename mapping performed by this feature (R5), which parallel branches use
  to rebase mechanically.
- **Enforcement** (FR-007, SC-002): a new structural rule in `Grimoire.ArchTests`
  (`AgentArtifactNamingRuleTests`, rule id **N1**), in the standard PR pipeline, with
  a Red/Green probe. Mechanics, two complementary parts:
  1. **Reference-based ownership detection** for the shared test/eval assemblies
     (`Grimoire.IntegrationTests`, `Grimoire.AgentEvals`, `Grimoire.ArchTests`,
     `Grimoire.EvalRunner` scenario types): via Mono.Cecil (same idiom as the ADR-006/
     ADR-009 rules), compute for each top-level type the set of agent-owned
     namespaces/assemblies it references (`Grimoire.IngestAgent[.*]`,
     `Grimoire.QueryAgent[.*]`, agent-owned Hub namespaces per the ownership map). A
     type referencing exactly **one** agent must carry that agent's token in its name;
     a curated exemption list (each entry justified as cross-agent in the convention
     document, mirrored in the test) covers edge cases.
  2. **Namespace ownership map** for `Grimoire.Hub`: an explicit map (ingest-owned:
     `IngestSubmission`, `IngestDispatch`, `IngestTaskArtifact`, …; query-owned:
     `QueryDispatch`, `QuerySubmission`, `QueryRunArtifact`, …; cross-agent:
     `AgentDispatch` (shared port + adapter), `Realtime`, `Runtime`, `ContentRoot`,
     `OperationalState`, `Conversion`) asserted so agent-owned types cannot live in
     unprefixed namespaces and vice versa.

**Alternatives considered**: a CI shell script over file names (rejected: not
type-aware, no reference analysis, weaker than the ArchTests idiom the constitution
already standardizes on); an .editorconfig/analyzer naming rule (rejected: cannot
express "references exactly one agent").

## R5 — Rename inventory (provisional old→new mapping)

The authoritative mapping is finalized in the convention document during
implementation (the N1 rule flags the complete set mechanically); the survey found
these violations of FR-005/FR-006 (unprefixed but single-agent-owned):

| Current (unprefixed / misplaced) | Owner | New name |
|---|---|---|
| `Grimoire.AgentEvals/ReplayEvalTests.cs` (`ReplayEvalTests`) | Ingest | `IngestReplayEvalTests.cs` (`IngestReplayEvalTests`) — the rename that motivated US2 (sibling `QueryReplayEvalTests` already prefixed) |
| `Grimoire.EvalRunner/Scenarios/ScenarioDefinitions.cs` (`ScenarioDefinitions`, `ScenarioDefinition`) | Ingest | `IngestScenarioDefinitions` / `IngestScenarioDefinition` (scenario **ids/slugs and recording directories stay unchanged** — see R7) |
| `Grimoire.ArchTests/GuardedWriteBoundaryRuleTests.cs` | Ingest (scans Ingest host + shared runtime) | `IngestAgentGuardedWriteBoundaryRuleTests.cs` (sibling `QueryAgentGuardedWriteBoundaryRuleTests` already prefixed; shared-runtime coverage remains inside both rules) |
| `Grimoire.IntegrationTests`: `ObservabilityLogTests`, `ObservabilityMetricsTests`, `ObservabilityTraceTests` | Ingest | `IngestObservability{Log,Metrics,Trace}Tests` |
| `AgentRunLifecycleTests`, `AgentTaskArtifactTests` | Ingest | `IngestRunLifecycleTests`, `IngestTaskArtifactTests` |
| `InstructionContextTests`, `InstructionLoadFailureTests`, `UserPromptTests` | Ingest | `Ingest`-prefixed equivalents |
| `RunQueueTests`, `RunSupervisionTests`, `RunActivityRealtimeTests`, `FailureAndReconciliationTests` | Ingest (single-slot FIFO dispatch) | `Ingest`-prefixed equivalents |
| `ConvertStepTests`, `SourceArtifactPersistenceTests`, `SubmissionPromptApiTests` | Ingest (submission pipeline) | `Ingest`-prefixed equivalents |
| `Grimoire.Hub.AgentDispatch.IngestRunCoordinator` + `IngestAgentRequest` | Ingest | move to `Grimoire.Hub.IngestDispatch` (namespace); `IAgentProcessLauncher`, `AgentRunEvent`, `Adapters.AgentProcess` **stay** in cross-agent `Grimoire.Hub.AgentDispatch` — port owner namespace unchanged, ADR-010/011 tables intact |
| `Grimoire.Hub.AgentDispatch.QueryAgentRequest` | Query | move to `Grimoire.Hub.QueryDispatch` |
| `Grimoire.Hub.Submission` (`SubmissionService`, `SubmitSourceOptions`) | Ingest | merge into `Grimoire.Hub.IngestSubmission` |
| `Grimoire.Hub.TaskArtifact` (`HubTaskArtifactDocument/Writer`) | Ingest | `Grimoire.Hub.IngestTaskArtifact` |
| `Grimoire.IngestAgent/AgentCliOptions.cs` (`AgentCliOptions`) | Ingest (in-assembly, but sibling is `QueryCliOptions`) | `IngestCliOptions` |
| `Grimoire.IngestAgent.AgentCore` namespace (holds only `IngestAgentInstrumentation` post-008) | Ingest | dissolve into `Grimoire.IngestAgent` |
| `Grimoire.IngestAgent/TelemetryBootstrap.cs`, `Grimoire.QueryAgent/QueryAgentTelemetryBootstrap.cs` | — | deleted; replaced by shared `AgentTelemetryBootstrap` (R2) |

Classified **cross-agent, stays unprefixed** (convention document lists these with
justification): `Grimoire.AgentRuntime.*` (the platform itself), `Grimoire.EvalRunner`
capture/replay/workspace/scoring machinery, `Grimoire.AgentEvals` infra tests
(`ReplayContractTests`, `StalenessTests`, `CaptureHygieneTests`,
`EvalProviderResolverTests`, `TimeoutEnforcingModelClientTests`, `SyntheticRecordings`,
`LocalEnvFileTests`), `Grimoire.IntegrationTests` platform/guardrail tests
(`PathTraversalTests`, `PolicyMisconfigurationTests`, `TraceContextPropagationTests`,
`HubRequestTracingTests`, shared `Fakes/`), `CredentialScopingTests` **if** it covers
both agents' spawns (else renamed), Hub cross-agent namespaces (R4.2), and the
`data/agents/<agent>/` instruction folders (already convention-conformant).

Frontend and Hub HTTP/SignalR route strings are out of scope (spec Assumptions);
renames touch code identifiers and file/namespace names only — never route paths,
event names, artifact schemas, or metric/span identities.

## R6 — Structural enforcement of "no duplicate platform concerns" (SC-001)

**Decision**: two new structural rules in `Grimoire.ArchTests`, each with a Red/Green
probe, alongside N1:

- **D1 (telemetry-bootstrap containment)**: in agent host assemblies
  (`Grimoire.IngestAgent`, `Grimoire.QueryAgent`, and any future `Grimoire.*Agent`),
  OpenTelemetry SDK provider-construction APIs (`Sdk.CreateTracerProviderBuilder`,
  `Sdk.CreateMeterProviderBuilder`, OTel `LoggerFactory` wiring) are forbidden — they
  are permitted only in `Grimoire.AgentRuntime.Telemetry`. This makes the ~65+28-line
  telemetry duplication structurally unrepeatable for agent three.
- **D2 (model-adapter composition containment)**: agent host assemblies must not
  construct `AnthropicModelClient`/`ReplayModelClient`/`TurnCaptureModelClient`
  directly; the only path is `Grimoire.AgentRuntime.Composition.ModelClientFactory`
  (invoked from each host's composition root — the ADR-012 selection semantics and
  env-var contract are unchanged, their implementation now exists once). Extends the
  existing C5-style "no concrete adapter references outside `.Adapters.`" discipline
  to the host assemblies.

D1/D2 plus the deletion of the duplicated scaffolds (R2) is how SC-001's "exactly one
implementation" is made enforceable rather than merely reviewed. (An exhaustive
"semantic duplicate detector" was rejected as unimplementable; the rules pin the two
concerns that demonstrably drifted, and FR-003's practical proof lands with
feature 013 per SC-005.)

## R7 — Behavior-preservation verification (SC-003) and rename-safety of recordings

**Decision**:

- The full pre-existing suite (integration, arch, domain unit, replay evals) runs
  unchanged apart from FR-006 renames and mechanical namespace-move updates; no
  assertion text weakened (enforced by review against the FR-009 rule; the spec's own
  acceptance scenario).
- **Observability identities are frozen**: service names, meter/source names, metric
  names, log event names + mandatory fields, span names/parents/attributes stay
  byte-identical (they are asserted by the existing in-memory-exporter tests per
  ADR-005 — those tests keep passing untouched, which *is* the identity check).
- **Replay recordings stay valid**: ADR-012 staleness fingerprints cover instruction
  surface, policy, fixture, scenario definition serialization, and judge prompt — none
  of which this feature touches. Renaming C# classes (`ScenarioDefinitions` →
  `IngestScenarioDefinitions`) must not change scenario **ids** or the
  `data/evals/recordings/<scenario>/` directory slugs; the green replay suite after
  the rename is the executable proof. Any fingerprint drift observed during
  implementation is a defect in the change, not an occasion to re-capture.
- Ingest and Query artifact/event shape comparison: the existing contract tests
  (task-artifact schema, query-run-artifact schema, NDJSON event tests, SignalR
  payload tests) already pin the shapes; they run unmodified.
