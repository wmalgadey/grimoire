# Implementation Plan: Wiki Structure Truth — Retire `pages/` and Report Real Wiki State

**Branch**: `022-align-wiki-structure` | **Date**: 2026-08-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/022-align-wiki-structure/spec.md`

## Summary

Feature 014 retired the `pages/` wrapper across the storage layout, the safety policies, and
the backend source tree, and locked it in with a structural rule that scans
`backend/src/**/*.cs`. The agent instruction files were never in that rule's scan surface, so
all three system prompts still navigate a folder that cannot exist. The query prompt goes
further and asserts something false — that `list_files(".")` on the content root "is not
allowed" — when every shipped policy grants `{"pathPrefix": "."}` on read. The agent was never
blocked from enumerating the wiki; it was told it was, and pointed at `pages/` instead. That is
the whole of the reported "the wiki is currently empty" defect.

Four workstreams follow from that, plus one the investigation surfaced:

1. **Rewrite the three system prompts** so they describe the real content root — `index.md`,
   `log.md`, open-ended category folders — enumerate it directly, and resolve wikilinks by
   filename rather than by constructing a `pages/`-prefixed path.
2. **Retire "page" as project terminology** in favour of "article", down to metric names,
   task-artifact fields, and persisted record keys. Pre-1.0, so this is a clean break with no
   alias period.
3. **Name the reserved harness surfaces** (`tasks/`, `conversations/`, `findings/`,
   `remediation-tasks/`) as distinct from wiki content, and put the decision about whether
   agents may read them in the operator's hands, denied by default (ADR-023).
4. **Rewrite the structural rule** so it covers instruction files, docs and comments, and so it
   forbids the retired *term* in identifiers — today its tokenizer deliberately tolerates
   `pages_touched`.
5. **Re-capture the entire eval recording corpus.** ADR-012 fingerprints the instruction
   surface and `policy.json` as its staleness authority, and CI treats replay staleness as the
   merge gate for instruction changes. Workstreams 1 and 3 invalidate every recording for all
   three agents simultaneously. This is not optional and it is the single largest cost item.

A live second defect turned up alongside the reported one: the lint prompt emits findings whose
`targetPath` is `pages/<slug>.md`, and Remediation Execution Mode reads `targetPath` verbatim —
so every remediation run targeting an article currently fails its first read.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, `backend/Directory.Build.props`), TypeScript /
SvelteKit for the frontend (untouched by this feature)

**Primary Dependencies**: OpenTelemetry (traces, metrics, logs; OTLP exporter),
`Microsoft.Extensions.*` configuration and DI, Anthropic SDK via `Microsoft.Extensions.AI`,
`Microsoft.Data.Sqlite` (operational state), xunit + NetArchTest.Rules + Mono.Cecil (tests)

**Storage**: Markdown files in the wiki content root (articles, `index.md`, `log.md`, and the
four reserved harness surfaces); embedded SQLite for operational state. No schema change — no
SQLite table or column contains the retired term, verified against the DDL in
`OperationalStateRepository.cs:66-85`.

**Testing**: xunit. Four projects run in the standard PR pipeline (`.github/workflows/ci.yml`):
`Grimoire.ArchTests`, `Grimoire.Domain.UnitTests`, `Grimoire.IntegrationTests`,
`Grimoire.AgentEvals` (replay, zero-skips enforced). No mocking framework is referenced
anywhere and none may be introduced (Constitution Principle II).

**Target Platform**: Local developer machine and devcontainer; Hub as an ASP.NET Core process
spawning agent child processes.

**Project Type**: Backend-heavy web service plus spawned CLI agent processes; the frontend is
not touched.

**Performance Goals**: The rewritten structural rule lives in `Grimoire.ArchTests`, which
ADR-021 places in the Fast tier by construction. Broadening its scan surface from
`backend/src/**/*.cs` to instruction files, `docs/`, and repo-root markdown must keep the tier
in its low-single-digit-second budget.

**Constraints**: No migration and no backward compatibility (pre-1.0, spec clarification
2026-08-09) — renamed persisted fields and telemetry series need not stay readable under their
old names. Instruction files are constrained by ADR-007 to exactly one `system-prompt.md` per
agent, loaded verbatim, fail-closed, SHA-256 recorded; there is no include mechanism.

**Scale/Scope**: ~308 occurrences of the retired term across `backend/src/**/*.cs`; ~190 across
the three system prompts (ingest 75, lint 61, query 54); 22 ADRs audited, of which 9 need
amendment notes. Four reserved harness surfaces. 15 success criteria.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|-----------|------|--------|
| I — Domain architecture, hexagonal boundaries | No new external system, so no new port. `SafetyPolicy` stays dependency-free: the subtractive read scope takes plain strings, never an options or configuration type. Adapter containment unchanged. | **PASS** — ADR-023 records the "no new port" finding; H1 enforces the Domain purity half. |
| II — Pragmatic testing | Harness contracts tested hermetically against real filesystem/temp dirs and real spawned processes; agent judgment tested by evaluation with thresholds. Doubles are hand-rolled port fakes only (`FakeAgentProcess`, `FakeModelClient`). No mocking framework. | **PASS** |
| II — Success-criteria split | SC-001–005, SC-010, SC-011, SC-013, SC-014, SC-015 are deterministic guarantees; SC-006–009 and SC-012 are evaluation thresholds. No 100% guarantee is attached to an agent-judgment outcome. | **PASS** |
| III — ADR-driven, test-enforced | All 22 existing ADRs read. One new ADR (ADR-023) drafted and accepted before `/speckit-tasks`. Phase 0 writes the structural rules with Red/Green probes. | **PASS** |
| IV — Behavioral & observable | Every renamed and new signal enumerated below, each with implementation, deterministic test, and CI enforcement tasks. Contract tests obtain signals through `AddHubTelemetry`'s `configureTracing` hook — the production composition root — never a test-only provider. | **PASS** |
| V — Agentic core & deterministic harness | Every wiki-content judgment this feature touches stays in instruction files. The harness gains only guardrail and provenance capability. See the Agentic Boundary table. | **PASS** |

**Constitution version in force**: 1.8.0. This spec was authored 2026-08-09, after the
2026-08-09 amendment, so the production-wiring contract-test rule and the final-phase
completeness-audit task both bind this feature.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

All 22 ADRs were read in full. Nine constrain this feature materially:

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| ADR-002 | Ingest agent execution model | The operator grant set must reach the agent through the existing spawn contract — CLI arguments to the child process — not a new channel or a file the agent reads. |
| ADR-006 | Agent tool-use loop and guarded tool boundary | Fixes the tool surface at exactly `list_files`/`read_file`/`write_file`, so FR-004's "enumerate the content root" is an instruction change plus a policy prefix, never a new tool. Fixes read scope as prefix-allow-only with no exclusion concept — the gap ADR-023 fills. Denial semantics (deny at invocation, record with reason, run continues) are already as FR-016 requires. |
| ADR-007 | Agent instruction surface | One `system-prompt.md` per agent, loaded verbatim, fail-closed, SHA-256 recorded. **No include mechanism** — Option 3 was rejected precisely to forbid a structural split. This constrains how SC-005 can be satisfied (see below). Query has no `default-user-prompt.md`; do not add one. |
| ADR-012 | Eval runner and recorded replay | Fingerprints the instruction surface and `policy.json` as the staleness authority; replay staleness in the PR pipeline is the merge gate for instruction changes. Rewriting three prompts invalidates every recording for all three agents. Re-capture is mandatory, not discretionary. |
| ADR-013 | Unified agent platform packaging and naming | Per-agent OTel identities (service names, `*_agent.*` span prefixes, `task_id`/`turn_id`) are frozen — none contains the retired term, so all stay untouched. The N1 agent-artifact naming rule and its convention-document exemption discipline are the model for the rewritten structural rule. |
| ADR-016 | Lint write scope: frontmatter-only enforcement | Its entire justification for a lexical rather than YAML frontmatter split rests on the ingest prompt's frontmatter convention: *"every page in this wiki is already required to open with exactly that shape."* The rewrite must preserve that convention in substance or Lint's writes begin failing `frontmatter_only_malformed_document`. |
| ADR-017 | Structural format enforcement for `log.md` and `index.md` | The rewritten catalog instructions must still produce lines matching `^- \[.+\]\(.+\) — .+ — .+$`, and log entries `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`, or the guard denies them. Its `log.md` check already exempts "the file does not yet exist and this is the first write" — so FR-013 needs **no guard change**. |
| ADR-018 | Human-authorized remediation action execution | Remediation context reaches the agent as Hub-injected CLI arguments, not guarded `read_file` calls, so denying `remediation-tasks/` does not break message-turn mode. This asymmetry must be stated, not discovered. |
| ADR-022 | Minimal directory configuration surface | Caps CLI **path** switches at three (structurally enforced); prescribes `appsettings.json` for everything else. Makes `policy.json` a build-distributed developer source, not an operator surface — which is why the grant set cannot live there. It is also the ADR that put the four harness surfaces inside the wiki root in the first place. |

**New ADR required?**: **Yes — [ADR-023: Operator-Controlled Read Scope over Reserved Harness
Surfaces](../../docs/adr/ADR-023-operator-controlled-harness-surface-read-scope.md), status
Accepted.**

Justification: FR-014 adds the first *deny* concept to a read model documented in code as
"allow-list-only with first-match-wins and no deny-rule concept," and adds a second authority
over a guardrail decision alongside the policy file whose SHA-256 is the existing provenance
mechanism. ADR-015 required a full ADR to add one optional field to the write-rule schema;
ADR-016 required one to add a single enum value. This is larger than either.

**Hexagonal gate**: No new external system. Policy evaluation is pure in-memory domain logic;
`policy.json` reading is a local-filesystem concern covered by Principle I's persistence
exemption; the grant set reaches the agent over the already-existing `IAgentProcessLauncher`
port. No new port, no new adapter namespace. ADR-023 records this finding explicitly.

**SC-005 versus ADR-007** — the one place the spec and an accepted ADR pull against each other.
SC-005 requires the content-root composition to be documented "in exactly one place, referenced
from every other place that needs it." ADR-007 forbids an include mechanism, so the three
prompts cannot reference a shared file at runtime. Resolution: `docs/conventions/wiki-content-root.md`
is the single authoritative document; each system prompt **restates** it in full for the agent's
runtime context and names the document as the source of truth; the structural rule checks that
no prompt reintroduces the retired concept, and a fixture mirrors the document the way
`AgentArtifactNamingRuleTests` already mirrors `docs/conventions/agent-artifact-naming.md`.
"Exactly one place" is satisfied for human readers and for enforcement; verbatim restatement in
the prompts is what ADR-007 requires and is machine-checked rather than trusted.

## Agentic Boundary (Constitution Principle V)

| Capability | Side | Where it lives |
|------------|------|----------------|
| Which category folder an article belongs in; when to create a new category | Agentic core | `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` |
| How to discover what the wiki contains (enumerate the root, read the catalog, resolve wikilinks) | Agentic core | all three `Instructions/system-prompt.md` |
| Whether the wiki is empty, and how to say so honestly | Agentic core | `Grimoire.QueryAgent/Instructions/system-prompt.md` |
| Whether an answer contains a Synthesis worth preserving | Agentic core | `Grimoire.QueryAgent/Instructions/system-prompt.md` (unchanged in substance; terminology only) |
| Which articles are orphans, undertagged, or stale | Agentic core | `Grimoire.LintAgent/Instructions/system-prompt.md` |
| Creating a missing `index.md`/`log.md` on first write | Agentic core | instruction change only — ADR-017's guard already permits first-write creation |
| Reserved harness surface names and that they are not article categories | Harness (stated to the agent) | `docs/conventions/wiki-content-root.md`, restated in each prompt; enforced at the tool boundary |
| Whether an agent may read a harness surface | Harness | `HarnessSurfaceReadOptions` → `SafetyPolicy` denied-read subtrees |
| Denying an ungranted harness-surface read and recording the reason | Harness | `Grimoire.Domain/Guardrails/SafetyPolicy.cs`, `AgentRuntime/Guardrails/GuardedToolExecutor.cs` |
| Recording the effective read scope of a run | Harness | task artifact, terminal run event, conversation record |
| Detecting reintroduction of the retired concept or term | Harness | `Grimoire.ArchTests` |

**Boundary smell test**: every behavioural change to what agents *do* with the wiki in this
feature is an instruction-file change. The backend changes are guardrail, provenance, naming,
and enforcement — no wiki-content judgment moves into code. FR-013's create-on-first-write is
deliberately an instruction change, not a harness bootstrap step, precisely to keep it on the
correct side.

## Test Strategy

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|-------------------|----------|-------------------|----------------------------------|-------------------------|-------|
| SC-001 (instruction files describe the real root; zero live `pages/` instructions) | Deterministic guarantee | Architecture test (text scan) | none — reads repo files | synthetic `ScanTarget` for the probe | `RetiredPagesWrapperPathRuleTests` over `backend/src/*/Instructions/**/*.md` plus docs and repo-root markdown |
| SC-002 (articles land at `<category>/<slug>.md`; catalog links resolve) | Deterministic guarantee | Hermetic integration test | `FakeModelClient` scripting a write; real filesystem in a temp wiki root | scripted ingest turn creating one article + index + log | Asserts the on-disk path has no wrapper segment and that the catalog link target exists |
| SC-003 (retired concept or term reintroduced anywhere covered → build fails, file named) | Deterministic guarantee | Architecture test + Red/Green probe | none | synthetic targets: a prompt containing `list_files("pages/")`; a source containing `CreateCounter<long>("wiki.ingest.pages_touched_total")` | Probe is a permanent second `[Fact]` per rule class, feeding synthetic text rather than mutating the repo |
| SC-004 (accepted decision records pass unmodified) | Deterministic guarantee | Architecture test | none | synthetic ADR fragment carrying a retirement marker | Pins FR-010 so a future tightening cannot start failing accepted ADRs |
| SC-005 (content-root composition documented in exactly one place) | Deterministic guarantee | Architecture test (fixture mirror) | none | `docs/conventions/wiki-content-root.md` | Mirrors `AgentArtifactNamingRuleTests.ExemptionFixture_MustMirror_TheConventionDocument`; fails on drift in either direction |
| SC-006 (≥95% of query runs against a populated wiki name a real category and article; ≤2% assert emptiness) | Agent-judgment threshold | Evaluation with threshold | recorded LLM responses (ADR-012 replay) | new eval scenario: populated fixture wiki, "what does the wiki cover?" | Deterministic scorer checks named categories/articles against the fixture's real filenames |
| SC-007 (≥90% of runs against an empty root report no articles without referring to `pages/`) | Agent-judgment threshold | Evaluation with threshold | recorded LLM responses | new eval scenario: content root with only the four harness surfaces — reproduces the reported failure exactly | Deterministic scorer: answer contains no `pages/` token and does not attribute emptiness to a missing folder |
| SC-008 (≥95% present no harness record as an article, cite none as a source) | Agent-judgment threshold | Evaluation with threshold | recorded LLM responses | fixture wiki with articles **and** populated harness surfaces, grant enabled | Scorer checks citations against the set of real article slugs |
| SC-009 (≥95% of ingest runs place the article in a non-reserved category) | Agent-judgment threshold | Evaluation with threshold | recorded LLM responses | existing ingest eval samples, re-captured | Deterministic scorer: created path's first segment ∉ reserved set |
| SC-010 (default install: 100% of harness-surface reads denied, recorded with a reason, run continues) | Deterministic guarantee | Hermetic integration test | `FakeModelClient` scripting `list_files("tasks")` and `read_file("conversations/x.md")`; real temp wiki root | default configuration (nothing set) | Asserts `DeniedActionRecord` with reason `harness_surface_not_granted` and that the run reaches a terminal state |
| SC-011 (100% of runs record which surfaces were permitted) | Deterministic guarantee | Hermetic integration test through production wiring | `FakeAgentProcess` / real spawned agent | grants set to a partial combination | Asserts the recorded grant set on the task artifact, the terminal event, and the conversation record — obtained via the real Hub composition, not a hand-built document |
| SC-012 (≥95% of granted-surface runs cite no harness record as a wiki source; ≤2% derive an article from one) | Agent-judgment threshold | Evaluation with threshold | recorded LLM responses | fixture with a granted surface and a tempting record | Scorer: created article's content overlap against harness record text |
| SC-013 (100% of ingest runs against a root lacking both files leave both present and populated) | Deterministic guarantee | Hermetic integration test | `FakeModelClient` scripting article + index + log writes; real temp wiki root with neither file | empty content root | Verifies ADR-017's first-write exemption covers the real path end to end |
| SC-014 (100% of metric, artifact-field and record-field names use the canonical term) | Deterministic guarantee | Architecture test | none | — | `WikiContentTerminologyRuleTests` |
| SC-015 (renamed signals report identical values before and after) | Deterministic guarantee | Hermetic integration test through production wiring | in-memory exporter attached via `AddHubTelemetry(configureTracing:)`; `FakeModelClient` | one scripted run exercised twice against the paired fixture | The rename changes the name and nothing else — same count, same labels, same trigger |

**Doubles**: `FakeModelClient` and `FakeAgentProcess` are existing hand-rolled fakes
implementing existing port interfaces. No new double is introduced, and no double is added to
isolate an internal collaborator. All verification is state-based — persisted files, parsed
records, exported telemetry — never interaction sequences.

**Production wiring (Constitution Principle IV, 2026-08-09 amendment)**: every observability
contract test in this feature obtains its signals by passing `AddInMemoryExporter` into
`TelemetryExtensions.AddHubTelemetry`'s `configureTracing` parameter, which attaches to the same
`TracerProviderBuilder` the application uses — so spans are observed under the real sampler and
the real instrumentation. Tests registering a process-wide `ActivityListener` join the
`HubActivityListenerObservability` or `IngestAgentObservabilityListeners` collection
(`DisableParallelization`), per the race documented in feature 019.

**Eval re-capture**: workstreams 1 and 3 change every fingerprinted input. All ingest, query and
lint recordings under `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/` must be
re-captured in the same change, or the PR pipeline's zero-skip replay gate fails by design.

## Observability

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|-------------|------|-------------|--------|
| `wiki.ingest.articles_touched_total` | Counter | Wiki articles created, updated, or superseded (**renamed** from `wiki.ingest.pages_touched_total`; declared in 001 and 002 plans) | `action=created\|updated\|superseded` |
| `wiki.query.synthesis_articles_created_total` | Counter | Synthesis Articles successfully created by a Query turn (**renamed** from `wiki.query.synthesis_pages_created_total`; declared in the 012 plan) | none |
| `wiki.ingest.harness_surface_reads_denied_total` | Counter | Reads of a reserved harness surface denied because the operator has not granted it (**new**) | `surface=tasks\|conversations\|findings\|remediation-tasks` |
| `wiki.query.harness_surface_reads_denied_total` | Counter | As above, Query agent (**new**) | `surface=…` |
| `wiki.lint.harness_surface_reads_denied_total` | Counter | As above, Lint agent (**new**) | `surface=…` |
| `hub.lint.inbound_links_refreshed_total` | Counter | Description text only: "Articles whose inbound-link count was updated" (**description renamed**, metric name unchanged) | unchanged |

The existing `wiki.<agent>.actions_denied_total` continues to fire for these denials through the
same `RecordDenied` funnel; it carries no surface label, which is why SC-010 needs the dedicated
counter.

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|-------|-------|---------|-----------------|
| `ingest.agent.completed` | INFO | Agent loop reached `end_turn` within caps (**existing event, three fields renamed**) | `task_id`, `turns`, `articles_created`, `articles_updated`, `articles_superseded`, `denials` |
| `wiki.query.synthesis_article_created` | INFO | A Synthesis Article write was allowed and committed (**renamed** from `wiki.query.synthesis_page_created`) | `task_id`, `turn_id`, `target_path` |
| `guardrails.harness_surface_read_denied` | WARN | A `list_files`/`read_file` call resolves inside a reserved harness surface the operator has not granted (**new**) | `task_id`, `agent`, `surface`, `requested_target`, `canonical_target`, `reason`, `turn` |
| `agent.harness_surface_scope_resolved` | INFO | Agent composition resolves the effective grant set at run start (**new**) | `task_id`, `agent`, `granted_surfaces`, `denied_surfaces` |

**Derivation rule (MANDATORY)**: every row above maps to three task categories in `tasks.md` —
implementation with stable event name and mandatory fields; deterministic integration tests
validating event name, level and every mandatory field; and CI enforcement. The CI half is
satisfied by placement in `Grimoire.IntegrationTests`, which
`.github/workflows/ci.yml` already runs, plus an explicit task recording that fact.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|-----------|-------------|-----------|
| `ingest_agent.resolve_harness_surface_scope` | `ingest_agent.run` | `task_id`, `granted_surfaces`, `denied_surfaces` |
| `query_agent.resolve_harness_surface_scope` | `query_agent.run` | `task_id`, `turn_id`, `granted_surfaces`, `denied_surfaces` |
| `lint_agent.resolve_harness_surface_scope` | `lint_agent.run` | `task_id`, `granted_surfaces`, `denied_surfaces` |
| `<agent>_agent.tool_call` | `<agent>_agent.run` | **existing span, new attributes** when a harness-surface denial occurs: `harness_surface`, `denial_reason=harness_surface_not_granted` |
| `ingest_agent.finalize_artifact` | `ingest_agent.run` | **existing span, renamed attributes**: `articles_created`, `articles_updated`, `articles_superseded` |

**Derivation rule (MANDATORY)**: every row maps to implementation, deterministic
parent/child-and-attribute tests correlated by `task_id`, and CI enforcement tasks. Trace tests
must obtain spans through the production composition root per the Constitution's 2026-08-09
production-wiring rule — the feature-003 failure mode (spans dropped by the real `ParentBased`
sampler while a test-only always-on provider showed green) is exactly what this feature's
renames could otherwise reproduce silently.

## Project Structure

### Documentation (this feature)

```text
specs/022-align-wiki-structure/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── wiki-content-root.md
│   ├── harness-surface-read-scope.md
│   └── terminology-rename-map.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
backend/src/
├── Grimoire.Domain/
│   └── Guardrails/
│       ├── SafetyPolicy.cs                    # CHANGED: denied-read subtrees, checked before the allow loop
│       └── PolicyDecision.cs                  # CHANGED: harness_surface_not_granted documented
├── Grimoire.AgentRuntime/
│   ├── Guardrails/
│   │   ├── GuardedToolExecutor.cs             # CHANGED: surface-labelled denial instrumentation
│   │   ├── DeniedActionRecord.cs              # CHANGED: reason vocabulary doc
│   │   └── IToolCallInstrumentation.cs        # CHANGED: renamed synthesis event, new denial hook
│   └── Telemetry/AgentTelemetryBootstrap.cs   # unchanged
├── Grimoire.Hub/
│   ├── HarnessSurfaces/                       # NEW: options record + reserved-name declaration
│   │   ├── HarnessSurfaceReadOptions.cs
│   │   └── ReservedHarnessSurfaces.cs
│   ├── HubHostComposition.cs                  # CHANGED: bind the new section
│   ├── appsettings.json                       # CHANGED: four grant keys, all false
│   ├── AgentDispatch/
│   │   ├── AgentRunEvent.cs                   # CHANGED: createdPages → createdArticles, grant set
│   │   └── Adapters/AgentProcess/AgentProcessHost.cs  # CHANGED: grant arg at all five spawn sites
│   ├── QueryConversations/
│   │   ├── ConversationRecordFormat.cs        # CHANGED: created_pages → created_articles, grant set
│   │   └── RecordedTurn.cs                    # CHANGED
│   ├── IngestTaskArtifact/HubTaskArtifactWriter.cs    # CHANGED: frontmatter keys
│   ├── OperationalState/RestartReconciler.cs  # CHANGED: frontmatter key
│   └── HubMetrics.cs                          # CHANGED: metric description
├── Grimoire.IngestAgent/
│   ├── Instructions/system-prompt.md          # REWRITTEN
│   ├── IngestAgentMetrics.cs                  # CHANGED: metric rename
│   ├── IngestAgentLogEvents.cs                # CHANGED: field renames
│   └── TaskArtifact/{TaskArtifactStore,TaskArtifactDocument}.cs   # CHANGED
├── Grimoire.QueryAgent/
│   ├── Instructions/system-prompt.md          # REWRITTEN
│   └── QueryAgentMetrics.cs                   # CHANGED: metric rename
├── Grimoire.LintAgent/
│   └── Instructions/system-prompt.md          # REWRITTEN
└── Grimoire.EvalRunner/
    ├── Scoring/{DeterministicScorers,LintDeterministicScorers}.cs # CHANGED + new 022 scorers
    ├── Capture/CapturePipeline.cs             # CHANGED
    └── Replay/{ReplayPipeline,QueryReplayPipeline}.cs             # CHANGED

backend/tests/
├── Grimoire.ArchTests/
│   ├── ArchScan.cs                            # CHANGED: shared FindRepositoryRoot()
│   ├── RetiredPagesWrapperPathRuleTests.cs    # REPLACES PagesWrapperRetirementBoundaryRuleTests
│   ├── WikiContentTerminologyRuleTests.cs     # NEW
│   └── HarnessSurfaceScopeRuleTests.cs        # NEW (ADR-023 H1/H2)
├── Grimoire.IntegrationTests/                 # NEW: read-scope, provenance, rename-invariance tests
└── Grimoire.AgentEvals/
    ├── Fixtures/recordings/                   # RE-CAPTURED: all three agents
    └── Fixtures/                              # NEW: empty-root and populated-root query fixtures

docs/
├── adr/ADR-023-operator-controlled-harness-surface-read-scope.md  # NEW (Accepted)
├── adr/ADR-{003,006,009,011,014,015,016,018}-*.md                 # CHANGED: amendment notes only
└── conventions/wiki-content-root.md           # NEW: the single authoritative layout document
```

**Structure Decision**: existing backend layout, unchanged. One new Hub namespace
(`Grimoire.Hub.HarnessSurfaces`) for the options record and the reserved-name declaration, and
one new conventions document. No new project, no new assembly, no new dependency.

## Complexity Tracking

No Constitution Check violations. One tension was resolved rather than accepted as a violation:

| Tension | Resolution | Simpler alternative rejected because |
|---------|-----------|--------------------------------------|
| SC-005 ("documented in exactly one place") vs ADR-007 (no include mechanism in instruction files) | One authoritative `docs/conventions/wiki-content-root.md`; each prompt restates it in full; a fixture-mirror architecture test fails on drift | Adding an include mechanism to the instruction surface would reopen ADR-007's rejected Option 3 and break its "editing one file provably edits the whole system prompt" guarantee |
| Two authorities over one guardrail decision (policy file + operator grant set) | ADR-023 makes the grant set the sole authority for harness surfaces and mandates recording it per run | Encoding grants as read-side `excludePrefixes` in `policy.json` would couple an operator boolean to the policy SHA-256, invalidating the whole eval recording corpus on every flip (ADR-012) |
