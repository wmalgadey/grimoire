---
status: accepted
---

# ADR-013: Unified Agent Platform Packaging and Agent-Artifact Naming Convention

## Context and Problem Statement

Feature 010 (unified-agent-platform) consolidates Ingest and Query onto one agent
platform ahead of the Lint agent (feature 013). Feature 008 extracted
`Grimoire.AgentRuntime` (ADR-011: loop, model-client port + Anthropic/Replay adapters,
guarded tool executor, instruction loaders, run-event emitter), but each agent host
still privately implements the remaining shared scaffold: OTel telemetry bootstrap
(68 lines in `Grimoire.IngestAgent/TelemetryBootstrap.cs` vs 63 near-identical lines
in `Grimoire.QueryAgent/QueryAgentTelemetryBootstrap.cs`), the tracing scaffold
(28 vs 27 lines), the ADR-012 model-adapter selection (`CreateModelClient`, ~30 lines
twice), error sanitization, CLI parsing, and the instructions→`started`→heartbeat
startup sequencing. That drift is exactly what a third agent would triple. In
parallel, artifact naming has drifted: Ingest-only artifacts created when Ingest was
the sole agent are unprefixed (`ReplayEvalTests`, `ObservabilityLogTests`,
`ScenarioDefinitions`, `Grimoire.Hub.TaskArtifact`, …) while their Query siblings are
prefixed (`QueryReplayEvalTests`, …), so a name no longer identifies ownership.

Two structural questions must be fixed by ADR before implementation:

1. **Packaging**: is "one platform" a shared library under near-identical thin
   per-agent host processes (the status quo shape, made uniform), or a single
   parameterized agent host process selecting a per-agent profile at spawn?
   ADR-011 chose separate processes specifically so that Query's lack of write
   capability was structural — "`write_file` is not compiled into the process at
   all". Feature 012 (query-synthesis-writes, spec on a parallel branch) will give
   Query guarded write capability, removing that premise, so the structural-no-write
   argument must not be treated as permanent. Spec 010 FR-004 requires the no-write
   guarantee to hold **as long as Query's profile declares no write capability**.
2. **Naming**: what convention maps an artifact's name to its owning agent, and how
   is it enforced (Constitution Principles III/IV: automated check, Red/Green probe,
   CI gate)?

## Decision Drivers

- Spec 010 FR-001/FR-002: every shared concern exactly once; per-agent code limited
  to the Agent Profile plus intent-specific artifact handling; no agent-conditional
  logic inside the platform.
- Spec 010 FR-008/FR-009/SC-003 (absolute behavior preservation): observable
  behavior, artifacts, events, and observability identities (metric/event/span/service
  names) unchanged; the full pre-existing suite passes with no weakened assertions.
- Spec 010 FR-004: capabilities are exactly what each profile declares, enforced at
  the guarded tool boundary; the Query no-write structural guarantee stays enforced
  with its probe while the profile declares no write tool — but must be phrased so
  feature 012 can renegotiate it without dismantling the platform.
- FR-003/SC-005: adding the Lint agent must require only a profile + instruction
  files + Hub dispatch surface.
- ADR-002/004/008/009/011 contracts (child-process spawn, per-spawn credential
  scoping, NDJSON event channel + supervision, explicit worker-path configuration,
  Ingest/Query dispatch shapes) must remain unbroken.
- Constitution Principles III/IV: every rule needs a structural test with a Red/Green
  probe and a CI gate; conventions not enforced by CI do not exist.
- Constitution Principle I: no extra assemblies/layers beyond what an ADR justifies;
  namespace-level containment is the default boundary strength.

## Considered Options

### Packaging

1. **Shared platform library, extended to own all remaining shared scaffold, under
   thin per-agent host executables** — `Grimoire.AgentRuntime` gains `Telemetry`,
   `Composition`, and `Host` namespaces; `Grimoire.IngestAgent`/`Grimoire.QueryAgent`
   shrink to Agent Profile + CLI option record + intent-specific artifact handling.
2. **Single parameterized agent host process** (`Grimoire.AgentHost --agent query …`)
   with per-agent profiles selected at spawn; per-agent projects dissolve.
3. Status quo (each host keeps its private scaffold) — rejected outright: it is the
   FR-001 violation this feature removes.

### Naming enforcement

- A. Structural rule in `Grimoire.ArchTests` (Mono.Cecil reference-based ownership
  detection + explicit Hub namespace-ownership map + curated, documented exemptions),
  Red/Green probed, in the standard PR pipeline.
- B. CI shell script over file names.
- C. Roslyn/editorconfig naming analyzers.

## Decision Outcome

Chosen: **Packaging option 1** and **naming enforcement option A**.

### Packaging: one platform library, thin per-agent hosts

- `Grimoire.AgentRuntime` becomes the complete Agent Platform:
  - `Grimoire.AgentRuntime.Telemetry` — `AgentTelemetryBootstrap` (OTel tracer/meter/
    logger providers + OTLP export, parameterized by the profile's service/source/
    meter names) and `AgentTracing` (ActivitySource holder, run-span helper
    parameterized by span name and correlation-attribute name). The per-agent OTel
    identities are **frozen inputs**, not new names: resource service
    `Grimoire.IngestAgent`/`Grimoire.QueryAgent`, spans `ingest_agent.*`/
    `query_agent.*`, attributes `task_id`/`turn_id`.
  - `Grimoire.AgentRuntime.Composition` — `ModelClientFactory` (the ADR-012
    replay/capture/live selection, implemented once; still invoked from each host's
    composition root, still driven by `GRIMOIRE_MODEL_REPLAY_PATH`/
    `GRIMOIRE_MODEL_CAPTURE_PATH` with fail-fast on both, with the profile supplying
    the per-agent model/base-url env-var names per ADR-004), `ErrorSanitizer`,
    `AgentArgumentReader` (CLI scaffold; each host keeps its own option record).
  - `Grimoire.AgentRuntime.Host` — `AgentProfile` (identity + telemetry identities +
    tool registry + required instruction documents + model env-var names) and the
    startup/shutdown template (fail-closed instruction+policy load → `started` →
    heartbeat → loop → terminal event), taking per-agent behavior exclusively through
    the profile and intent hooks (Ingest: task-artifact lifecycle, ingest log,
    source reading, rollback/all-denied handling, user-prompt resolution; Query:
    stdin conversation scaffold). No `if (agent == …)` inside the platform.
  - Existing `Core`/`Guardrails`/`RunEvents`/`Instructions` namespaces: unchanged.
- `Grimoire.IngestAgent` and `Grimoire.QueryAgent` remain **separate standalone
  executables** spawned per run exactly as today (ADR-002/ADR-011 spawn + credential
  contracts, ADR-009 worker-path configuration, ADR-008 event channel: all
  unchanged). Each contains only: `Program.cs` (composition root: profile declaration
  + intent hooks + `AgentHost` invocation), its CLI option record, its tool registry,
  its instrumentation adapters and frozen metric/log-event definitions, and its
  intent-specific artifact-handling namespaces.
- **Option 2 rejected for this feature, deferred not forbidden**:
  - It would invalidate the existing per-host structural no-write rule
    (`QueryAgentGuardedWriteBoundaryRuleTests`) ahead of feature 012, forcing a
    reformulated guarantee inside a change set whose defining constraint is "no
    weakened assertions" (FR-009).
  - It would merge two frozen OTel service identities into one binary and change the
    Hub's spawn/worker-path configuration — churn at observable boundaries in a
    feature whose value is being invisible at observable boundaries (FR-008).
  - It saves nothing: the per-intent modules (Ingest artifact lifecycle, Query
    conversation scaffold) persist either way, so a single host reduces zero
    duplicated lines beyond the library extraction; it only converts compile-time
    capability separation into runtime branching.
  - Revisit trigger: if after features 012/013 the per-agent hosts have degenerated
    into pure profile declarations with no intent-specific code, a follow-up ADR may
    collapse them into one parameterized host; this ADR records that as the natural
    evolution path, not a rejected end state.

### Capability guarantee (FR-004), restated to survive feature 012

An agent's effective capabilities are **exactly its profile's declared tool
registry**, enforced at the guarded tool boundary (`GuardedToolExecutor` +
deny-by-default policy) at invocation time — this is the durable, packaging-
independent formulation. Today, and for as long as the Query profile declares no
write tool:

- `QueryToolRegistry` remains `list_files` + `read_file` only; no write tool is
  registered in the Query host.
- The per-host structural rule (filesystem-write APIs unreachable from the Query
  host's tool-dispatch path; `QueryAgentGuardedWriteBoundaryRuleTests` with its
  Red/Green probe) remains in force unchanged.

When feature 012 gives Query guarded write capability, it changes the Query
**profile, policy, and that structural rule** via its own ADR — the platform
packaging fixed here is unaffected. The "no write tool compiled into the process"
framing of ADR-011 is hereby demoted from architectural premise to current profile
state.

### Naming convention and enforcement

- A versioned convention document, `docs/conventions/agent-artifact-naming.md`,
  states: every agent-specific code artifact (test files/classes, evaluation suites,
  namespaces, per-agent components, instruction folders) carries its agent's name;
  unprefixed names are reserved for genuinely cross-agent artifacts (used by ≥2
  agents or by the platform/harness itself — e.g. a fixture shared by ingest and
  query tests stays unprefixed). It contains the complete old→new rename mapping of
  feature 010 (provisional inventory: `specs/010-unified-agent-platform/research.md`
  R5; headline rename: `ReplayEvalTests` → `IngestReplayEvalTests`) so parallel
  branches rebase mechanically.
- Enforcement in `Grimoire.ArchTests`, standard PR pipeline, Red/Green probed:
  - **N1 (agent-artifact naming)**: Mono.Cecil scan of the shared assemblies
    (`Grimoire.IntegrationTests`, `Grimoire.AgentEvals`, `Grimoire.ArchTests`,
    `Grimoire.EvalRunner`): a top-level type referencing exactly one agent's
    assemblies/owned namespaces must carry that agent's token in its name, modulo a
    curated exemption list mirrored from the convention document. For
    `Grimoire.Hub`, an explicit namespace-ownership map (ingest-owned / query-owned /
    cross-agent) is asserted the same way.
- Hub namespace moves that follow from the convention (`IngestRunCoordinator` et al.
  → `Grimoire.Hub.IngestDispatch`; `Grimoire.Hub.Submission` →
  `Grimoire.Hub.IngestSubmission`; `Grimoire.Hub.TaskArtifact` →
  `Grimoire.Hub.IngestTaskArtifact`; `QueryAgentRequest` →
  `Grimoire.Hub.QueryDispatch`) are pure moves. The `IAgentProcessLauncher` port, its
  `Adapters.AgentProcess` namespace, and `AgentRunEvent` **stay** in cross-agent
  `Grimoire.Hub.AgentDispatch`, so the ADR-010/ADR-011 port table and containment
  rule C4 are untouched. Existing arch rules whose namespace anchors move are updated
  with the move and re-probed (spec FR-010: the rule moves with the boundary).

### Duplication containment (new structural rules)

- **D1 (telemetry-bootstrap containment)**: in agent host assemblies
  (`Grimoire.*Agent`), OTel SDK provider-construction APIs are permitted only via
  `Grimoire.AgentRuntime.Telemetry` — re-implementing a private telemetry bootstrap
  in a host fails the build.
- **D2 (model-adapter composition containment)**: agent host assemblies must not
  construct `AnthropicModelClient`/`ReplayModelClient`/`TurnCaptureModelClient`
  directly; the only path is `ModelClientFactory` (composition-root invocation).
  This carries ADR-012's "selection only in the composition root" intent into a
  single shared implementation; ADR-012's env-var contract and fail-fast semantics
  are unchanged.

N1, D1, D2 each ship with a Red/Green probe before feature code (Constitution
Principle III; note ADR-011 and ADR-012 both used a "C6/C7" numbering — this ADR
starts fresh letters to avoid extending that collision).

### Relationship to ADR-011 (partial supersession)

**Superseded (packaging / runtime-sharing aspects only)**:

- ADR-011's enumeration of what `Grimoire.AgentRuntime` contains is extended: the
  library now also owns telemetry bootstrap, tracing scaffold, model-client
  composition, error sanitization, CLI scaffold, and the host startup template
  (`Telemetry`/`Composition`/`Host` namespaces). "Near-identical thin host" replaces
  "each process supplies its own …" as the description of what a host is.
- ADR-011's rationale that Query's no-write guarantee rests on `write_file` being
  "not compiled into the process at all" is superseded by the profile-declared
  formulation above (guarantee unchanged today; premise no longer load-bearing).

**Left intact (not reopened here)**:

- The separate-process execution model itself, spawn/CLI/credential contracts
  (ADR-002/004), and the ADR-009 path configuration.
- Streaming (`answer_chunk` event, `IModelClient` delta callback), Hub-side bounded
  concurrency (`QueryRunCoordinator`, reject-over-limit) vs Ingest's single-slot
  FIFO, interruption semantics, and realtime delivery (`QueryLifecycleHub`).
- Query Run Artifact ownership/location and the no-server-side-conversation-store
  decision. **Deliberately untouched**: feature 011 (query conversation records) is
  planning to revisit query-turn persistence under its own ADR-014 — conversation/
  artifact persistence questions belong there, not here; feature 011 is context
  only for this ADR.
- The `IModelClient`/`IAgentProcessLauncher` port table (as amended by ADR-011) and
  containment rules C6/C7.

### Consequences

- Good: every shared concern exists exactly once (FR-001/SC-001); the Lint agent
  becomes profile + instruction files + Hub dispatch surface (FR-003), with D1/D2
  making scaffold re-duplication a build failure rather than a review catch.
- Good: zero observable change — processes, spawn contracts, artifacts, events, and
  all observability identities are preserved by construction, so the existing suite
  keeps passing without weakened assertions (FR-008/FR-009).
- Good: name-identifies-owner becomes machine-enforced (N1), and the convention
  document's rename map gives parallel branches (011, 012, 013) a mechanical rebase
  recipe.
- Bad: another move-heavy refactor breaks `git blame` continuity for the scaffold
  files and renamed tests; mitigated by move-only/rename-only commits (same tradeoff
  accepted in ADR-010/ADR-011).
- Bad: N1's exemption list is a curated artifact that can rot; mitigated by requiring
  each exemption to carry a justification in the convention document and by the
  Red/Green probe proving the rule actually bites.
- Bad: renames force in-flight branches to rebase; mitigated by the old→new mapping
  and by merging feature 010 first (spec sequencing assumption).
- Neutral: the single-parameterized-host option remains available later via a new ADR
  once per-agent hosts contain no intent-specific code; nothing here forecloses it.

## More Information

Detailed rationale: `specs/010-unified-agent-platform/research.md` (R1–R7). Plan:
`specs/010-unified-agent-platform/plan.md`. Per Constitution (Spec-Kit Workflow
step 4) this ADR MUST reach **Accepted** status via explicit project-owner sign-off
before `/speckit-tasks` runs for feature 010 — it is intentionally left `proposed`
by the planning run rather than author-accepted, because it partially supersedes an
accepted ADR (ADR-011) and interacts with the in-flight features 011 and 012.
