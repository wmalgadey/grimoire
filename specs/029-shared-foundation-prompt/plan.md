# Implementation Plan: Shared Foundation Prompt and Wiki-Identity Wizard

**Branch**: `claude/shared-foundation-prompt-j3jdwi` | **Date**: 2026-09-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/029-shared-foundation-prompt/spec.md`

## Summary

Every agent's system prompt becomes two documents instead of one: a shared **foundation document**
stating what kind of wiki this instance maintains and the conventions that hold across all agents, plus
the agent's own **role document**. The harness composes them in a fixed order — foundation first, one
blank line, role second — in the single shared startup template all three agents already run through.
The shipped default is delivered by the existing agent build into each agent's `Instructions/` folder;
an instance that wants a different wiki gets one file at `<DataDir>/foundation-prompt.md`, whose mere
presence makes it the effective document for every agent.

That file is put there by a **wiki-identity wizard in the Hub**, not by the deployment script. The
wizard asks one question; "default" writes nothing at all; "specialised" turns the operator's
description into a *drafting brief* that an agent session on the deploy host drafts from, and a second
invocation hands the drafted document back for the Hub to validate and persist verbatim. The Hub never
authors the text — it holds custody of bytes it received whole (ADR-056). `grimoire-server` gains thin
glue that starts the Hub's wizard and surfaces the identity the Hub reports.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`); Bash for the deployment-script glue

**Primary Dependencies**: no new package. Spectre.Console.Cli (ADR-048) for the wizard's command
surface; OpenTelemetry for the declared signals; the existing `SystemPromptLoader`, `AgentHost`,
`GrimoirePathResolver` and `AgentProcessHost` carry the composition work.

**Storage**: files on disk. The instance foundation document is one markdown file under the existing
data root; the default is build-distributed image/build content. No database change — the
operational-state database is untouched (Principle V: durable state lives in files).

**Testing**: xUnit. `Grimoire.ArchTests` (Boundary Rule, Phase 0), `Grimoire.IntegrationTests`
(behavioral/classicist, real filesystem in temp dirs, real spawned agent processes),
`Grimoire.AgentEvals` Tier=Fast for hermetic harness mechanics. No mocking framework, no new fake
beyond the existing port fakes.

**Target Platform**: Linux container (`deploy/Dockerfile`) and local development checkout.

**Project Type**: backend service with a spawned-agent runtime, plus a shell deployment helper.

**Performance Goals**: no measurable change. Composition adds one file read and one string join per
run, on a path that already reads two-to-three instruction files.

**Constraints**: the wizard must never block on a terminal (US3); the instance document must survive
redeploy/rollback/restart (FR-017); nothing in production code may author instruction content
(Principle V).

**Scale/Scope**: three agent types, one shared document, one wizard command with three answers, one
status line.

## Constitution Check

*GATE: passed before Phase 0 research; re-evaluated after Phase 1 design — see the re-check at the end.*

| Principle | Assessment |
|---|---|
| **I — Domain architecture & hexagonal boundaries** | No new external system is introduced. The drafting agent runs on the deploy host and is never invoked by the Hub (spec Assumptions), so no new port, adapter namespace or containment rule is needed. Infrastructure containment is untouched: the wizard uses the filesystem directly, which the persistence exemption already covers. **Gate: pass.** |
| **II — Pragmatic testing** | Harness contracts (composition, fail-closed load, resolution, custody, wizard outcomes) are tested deterministically and hermetically against real files in temp dirs and real spawned agent processes. Agent judgment (SC-008/SC-009) is classified lower-stakes in the spec and is covered by the user-reported correction loop, not a new eval suite. Classicist/state-based throughout; no mocking framework; no new double. **Gate: pass.** |
| **III — ADR-driven & test-enforced** | Three ADRs (053, 054, 056), all in the Mandatory format, each deciding one aspect; a fourth (055) was drafted and declined for recording feature behaviour rather than a decision. One Boundary Rule (instruction authorship, existing, widened) gets the Phase 0 Red/Green probe; everything else is tagged Feature-Scoped Invariant and covered behaviorally. **Gate: pass, conditional on the three remaining ADRs reaching Accepted before `/speckit-tasks`.** |
| **IV — Behavioral & observable** | `## Observability` below enumerates 2 metrics, 5 log events and 2 spans, each with the three task categories the constitution derives. No new infrastructure. **Gate: pass.** |
| **V — Agentic core & deterministic harness** | The foundation document is instruction content; the harness resolves, loads, composes, hashes and reports it and never interprets it. The wizard's content judgment is exercised by an agent session outside the system; the Hub's contribution is custody of bytes received whole, enforced structurally (ADR-056). Guardrails, write scopes, path roots and credential scope are untouched by document content (FR-010). **Gate: pass.** |

**No Complexity Tracking entries** — no gate required justification.

## Architectural Constraints & ADRs

*GATE: all ADRs in `docs/adr/` were read before this section.*

| ADR | Title | Constraint on this feature |
|-----|-------|---------------------------|
| [ADR-053](../../docs/adr/ADR-053-agent-system-prompt-composition.md) | An Agent's System Prompt Is a Shared Foundation Document Composed With Its Role Document | **New, drafted here.** Fixes the two-document surface, the composition order, verbatim/fail-closed loading, per-document recording, and the explicit CLI path per document. Supersedes ADR-007. |
| [ADR-054](../../docs/adr/ADR-054-default-user-prompt-and-message-scaffold.md) | Per-Run Steering Is a Versioned Default User Prompt Inside a Harness-Owned Scaffold | **New, drafted here.** Re-decides ADR-007's user-channel aspect unchanged in substance, as the whole-ADR supersession rule requires. Constrains this feature only by staying true: nothing here touches the user channel. |
| [ADR-055](../../docs/adr/ADR-055-foundation-document-resolution.md) | (declined) The Effective Foundation Document Is the Build-Distributed Default Unless an Instance Document Exists | **Drafted here and declined in review**: it fixed a file location and a presence check, which is feature behaviour, not a boundary or technology decision (Principle III, "no feature content"). It constrains nothing. Where the effective document comes from is fixed by [contracts/foundation-document.md](./contracts/foundation-document.md) and [data-model.md](./data-model.md) instead. |
| [ADR-056](../../docs/adr/ADR-056-instance-instruction-custody.md) | One Named Custodian May Persist an Instruction Document It Received Whole, and Nothing May Author One | **New, drafted here.** Permits exactly one component to write the instance document, only with bytes received whole, and keeps authorship forbidden everywhere. Widens the instruction-authorship Boundary Rule's allow-list by one entry — a change to what production code may do, enforced by a structural test, which is why this one *is* an ADR where ADR-055 was not. |
| [ADR-007](../../docs/adr/ADR-007-agent-instruction-surface.md) | Agent Instruction Surface (single system prompt) | **Superseded by ADR-053 + ADR-054 in this feature.** Its "entire system prompt, one file" decision is what this feature contradicts; `superseded_by` and `reason` are set on it in the same change. |
| [ADR-043](../../docs/adr/ADR-043-build-distributed-agent-artifacts.md) | Build-Distributed Agent Artifacts | Constrains where the default lives: the agent build delivers it, the Hub never seeds or materializes it. Pure extension ("new files in an agent's build output" is in its own Change Triggers) — no status change. |
| [ADR-041](../../docs/adr/ADR-041-independent-directory-roots.md) / [ADR-052](../../docs/adr/ADR-052-memory-directory-root.md) | Independent roots; memory root | Constrain where the instance document may live. It is a derived filename under the existing data root — no fifth root, and not under the memory root, whose scope is per-run bookkeeping. |
| [ADR-042](../../docs/adr/ADR-042-mandatory-configuration-file.md) | Mandatory Configuration File | Constrains the design negatively: **no new configuration key is introduced**, so this ADR is neither extended nor invalidated. The earlier draft's override key was dropped for exactly this reason. |
| [ADR-040](../../docs/adr/ADR-040-runtime-path-composition.md) | Runtime Path Composition at One Explicit Point | The new path is derived inside `GrimoirePathResolver` like every other; no other production type may compose it. |
| [ADR-012](../../docs/adr/ADR-012-eval-runner-recorded-replay.md) | Eval Runner and Recorded Replay | The fingerprint set follows the instruction surface, so it gains a `foundation_prompt` key; every existing recording goes stale and must be re-captured. Extension — no status change. |
| [ADR-013](../../docs/adr/ADR-013-unified-agent-platform-packaging-and-naming.md) / [ADR-044](../../docs/adr/ADR-044-shared-agent-runtime-library.md) | Unified agent platform; shared runtime library | Composition must live in the shared `AgentHost` template with no agent-conditional branch; the authored default document lives in the shared runtime library's `Instructions/`. |
| [ADR-036](../../docs/adr/ADR-036-agent-child-process-spawn-contract.md) | Agent Child-Process Spawn Contract | The new `--foundation-prompt-path` option follows the existing explicit-path contract; the Hub composes, the agent discovers nothing. |
| [ADR-048](../../docs/adr/ADR-048-hub-cli-framework.md) / [ADR-049](../../docs/adr/ADR-049-cli-in-process-blocking-execution.md) | Hub CLI framework; in-process blocking execution | The wizard is a Hub CLI command in the existing catalog, running in-process against the shared composition root, with the established exit-code convention. Extension of both. |
| [ADR-006](../../docs/adr/ADR-006-agent-tool-loop-guarded-boundary.md) / [ADR-030](../../docs/adr/ADR-030-guarded-retrieval-tool-surface.md) / [ADR-031](../../docs/adr/ADR-031-lint-full-wiki-write-scope.md) | Guarded tool boundary and write scopes | Constrain this feature negatively and absolutely: no document content may widen them (FR-010), and the instance document lives outside every agent-writable root so no agent can rewrite its own steering. |
| [ADR-034](../../docs/adr/ADR-034-path-and-subprocess-containment-hardening.md) | Path and Subprocess Containment Hardening | Unchanged. The feature spawns nothing new and adds no path the agent can reach. |
| [ADR-051](../../docs/adr/ADR-051-backend-test-tier-taxonomy.md) | Backend Test Tier Taxonomy | Places the new tests: ArchTests for the Boundary Rule, IntegrationTests for behaviour, Tier=Fast for hermetic mechanics. |

**New ADR required?**: **Yes — three**: ADR-053 (instruction surface), ADR-054 (user channel, forced by
the whole-ADR supersession of ADR-007) and ADR-056 (the authorship/custody Boundary Rule). All three
must reach **Accepted** before `/speckit-tasks` is invoked, and ADR-007 must carry
`status: superseded`, `superseded_by: [ADR-053, ADR-054]` and a `reason` in the same change.

A fourth was drafted and **declined in review**: ADR-055 recorded where the foundation document lives
and how it is resolved, which is feature behaviour rather than an architectural decision. The test that
separates them is what the record *constrains*: ADR-056 changes what production code is permitted to do
and is enforced by a structural test; ADR-055 changed where a file sits, which `plan.md`,
`data-model.md` and `contracts/foundation-document.md` are the right home for. The declined file is kept
as the record of the alternatives weighed (Principle III: ADR numbers are permanent, `declined` is a
first-class status).

## Resolution of the effective foundation document

Not an ADR (see above) — recorded here, with the full alternatives in
[research.md](./research.md) R1/R6 and the normative shape in
[contracts/foundation-document.md](./contracts/foundation-document.md):

- **Default**: `<AgentDir>/<agentId>/Instructions/foundation-prompt.md`, delivered by each agent's own
  build from one authored source, validated as a required input at startup, per agent.
- **Instance document**: `<DataDir>/foundation-prompt.md`, optional. When it exists it is the effective
  document for **every** agent; when it does not, each agent resolves its own build-distributed copy.
- **Presence-based**, deliberately: no configuration names this location, so there is no path to mistype
  and no configured-but-missing case that could silently fall back.
- **Per run**, at the point instruction paths are composed — a run dispatched after the file changes
  operates under the new content, with no restart.
- **No new configuration key, no new root, no new volume, no compose or Dockerfile change.** The data
  root is already volume-backed, which is what makes an instance document survive redeploy and rollback.
- Evaluation runs have no data root and therefore always operate under the repository-source default.

## Agentic Boundary (Constitution Principle V)

| Capability | Side | Where it lives |
|---|---|---|
| What kind of wiki this instance maintains; the conventions holding across all agents | Agentic core | `foundation-prompt.md` (shipped default: `backend/src/Grimoire.AgentRuntime/Instructions/`; instance: `<DataDir>/foundation-prompt.md`) |
| Each agent's role, steps, write scope and modes | Agentic core | `backend/src/Grimoire.<Agent>Agent/Instructions/system-prompt.md` |
| Turning an operator's description into a foundation document | Agentic core, **outside this system** | An agent session on the deploy host, drafting from the brief the Hub emits |
| Resolving which foundation document is effective | Harness | `GrimoirePathResolver` |
| Loading both documents verbatim, fail-closed, and composing them in the fixed order | Harness | `Grimoire.AgentRuntime/Host/AgentHost.cs` |
| Passing the resolved paths to the child process | Harness | `AgentProcessHost`, each agent's CLI options |
| Recording which documents a run operated under | Harness | task artifact `instruction_files`, run events |
| Producing the drafting brief from the operator's description | Harness | wizard command — mechanical assembly of *the operator's own words* plus the document's required shape; it states no wiki content of its own |
| Persisting a drafted document verbatim; refusing to clobber | Harness (custodian, ADR-056) | the wizard's custodian component |
| Reporting which identity is in effect | Harness | Hub CLI output, structured log events, `grimoire-server status` |

The one line that deserves scrutiny is the drafting brief. It is harness-side because it contains no
judgment about what a wiki should be: it is the operator's description, quoted, plus the document's
shape (headings and what each is for). If it ever grows a sentence that says what a good wiki does, it
has crossed the line and belongs in an instruction file instead.

## Test Strategy

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
|---|---|---|---|---|---|
| **SC-001** every run operates under both documents; both versions determinable afterwards | Deterministic guarantee | Integration test per agent type, real spawned agent process | none — real filesystem in a temp dir, existing port fakes only | temp agent dir with both documents | Asserts the run's recorded instruction entries name both documents with distinct versions |
| **SC-002** missing/unreadable/empty foundation document fails before any wiki write | Deterministic guarantee | Integration test, three variants (absent, unreadable, whitespace-only) | none | temp wiki root left empty | Asserts the failure reason names the foundation document and the wiki root is untouched |
| **SC-003** the agent operates under both documents byte-for-byte, in the documented order | Deterministic guarantee | Integration test asserting the composed instruction text | existing `FakeModelClient`-style port fake capturing the system prompt it received | two documents with distinctive marker lines | State-based: the captured text equals `foundation + "\n\n" + role`, byte-for-byte |
| **SC-004** choosing the default leaves the instance indistinguishable | Deterministic guarantee | Hub CLI integration test | none — real data dir in a temp dir | fresh instance | Asserts no file was created under the data root and resolution still reports the default |
| **SC-005** a re-run never silently replaces an existing document | Deterministic guarantee | Hub CLI integration test, two variants (no explicit decision → refused; explicit decision → replaced) | none | instance document already present, plus a hand-edited variant | Asserts exit code, message, and that the bytes on disk are unchanged in the refusal case |
| **SC-006** with no terminal, the wizard completes or fails — never blocks | Deterministic guarantee | Hub CLI integration test with no terminal attached | none | — | Asserts a bounded, non-blocking failure naming the missing answer |
| **SC-007** the instance document survives redeploy/rollback/restart | Deterministic guarantee | Integration test over the resolution path + a deployment-script test | none | data root persisted across two Hub startups | The container-level guarantee follows from the data root being volume-backed; the test proves resolution is stable across restarts of the resolving process |
| **SC-008** default content ⇒ behaviour indistinguishable from today | Lower-stakes agent judgment | Hermetic test of the plumbing (the composed text contains exactly the same statements as today's three prompts, reorganised) + user-reported correction loop | none | the extraction diff itself | **No eval suite required** (Principle II). The operator observes behaviour via the Hub's signals and the wiki, reports, instruction files are adjusted |
| **SC-009** instance document ⇒ agents' work reflects that wiki's purpose | Lower-stakes agent judgment | Hermetic test that the instance document is what reaches the agent + user-reported correction loop | none | an instance document with a distinctive convention | **No eval suite required**; "reflects" is operator judgment on the resulting wiki |
| **Boundary Rule** — nothing outside the allow-list writes an instruction filename | Deterministic guarantee | Structural test with Red/Green probe (Phase 0) | none — IL inspection | a deliberately bad class, added then deleted | Probe must cover the *new* literal specifically, not only the pre-existing ones |

**Recording refresh is a DoD step, not a test**: composing a second document changes the system-prompt
hash the replay client verifies, so every recorded scenario reports stale and CI's replay-eval step
fails until the recordings are re-captured against a live provider. That capture is operator-triggered
and cannot be completed by implementation. It is tracked as an explicit final-phase task.

## Observability

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
|---|---|---|---|
| `wiki.identity.foundation_resolved_total` | Counter | Foundation document resolutions, by which source was effective | `source=default\|instance` |
| `wiki.identity.wizard_outcomes_total` | Counter | Wiki-identity wizard invocations by terminal outcome | `outcome=default_kept\|brief_emitted\|document_persisted\|replace_refused\|rejected` |

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
|---|---|---|---|
| `wiki_identity_foundation_resolved` | INFO | Each time the effective foundation document is resolved for a run | `source`, `resolved_path`, `sha256`, `agent_id` |
| `wiki_identity_default_kept` | INFO | The wizard completes with the operator choosing the shipped default | `outcome` |
| `wiki_identity_brief_emitted` | INFO | The wizard produces a drafting brief from the operator's description | `description_length`, `brief_length` |
| `wiki_identity_document_persisted` | INFO | The custodian writes an instance document | `sha256`, `bytes`, `replaced_existing` |
| `wiki_identity_replace_refused` | WARN | A re-run would have replaced an existing document without an explicit decision | `existing_sha256`, `reason` |

**Derivation rule (MANDATORY)**: every row above maps to three task categories in `tasks.md` —
implementation with the stable event name and mandatory fields, a deterministic integration test
validating name/level/every mandatory field, and CI coverage in the standard PR pipeline (these tests
live in `Grimoire.IntegrationTests`, which `ci.yml` already runs).

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
|---|---|---|
| `hub.wiki_identity.wizard` | root (CLI invocation) | `answer`, `outcome`, `interactive` |
| `hub.wiki_identity.persist` | `hub.wiki_identity.wizard` | `sha256`, `replaced_existing`, `resolved_path` |

**Derivation rule (MANDATORY)**: both rows map to implementation, deterministic parent/child + attribute
tests, and CI coverage, as above. Per Principle IV the contract tests **must obtain their spans from the
production composition root** — the real telemetry registration, the real sampler, the real exporter
pipeline — never a test-only provider. This is the failure mode features 002 and 003 shipped; the tests
here start the Hub CLI the way production does.

Foundation resolution deliberately gets **no span of its own**: it happens inside the existing dispatch
span, and its log event is emitted within that active span context, so it is correlatable by
`task_id` without inventing a span that would only ever have one child-free node.

**Operator loop surface (Principle V)**: SC-008 and SC-009 rely on the correction loop, and the signals
above are consumed on two surfaces the operator already uses — the **Hub CLI's own output** (the wizard
reports what it did; the identity is printed by `grimoire-server status`, which reads it from the Hub)
and the **OpenTelemetry dashboard** shipped in `compose.yaml` (ADR-005), where
`wiki_identity_foundation_resolved` shows which document each run actually operated under.

## Project Structure

### Documentation (this feature)

```text
specs/029-shared-foundation-prompt/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── agent-cli.md
│   ├── wiki-identity-cli.md
│   └── foundation-document.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks output — not created here
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Grimoire.AgentRuntime/
│   │   ├── Instructions/
│   │   │   ├── foundation-prompt.md          # NEW — the single authored default
│   │   │   └── SystemPromptLoader.cs         # unchanged; loads either document
│   │   └── Host/AgentHost.cs                 # composition point (foundation + role)
│   ├── Grimoire.IngestAgent/
│   │   ├── Instructions/system-prompt.md     # loses the wiki-wide sections
│   │   └── IngestCliOptions.cs               # + --foundation-prompt-path
│   ├── Grimoire.QueryAgent/                  # same two changes
│   ├── Grimoire.LintAgent/                   # same two changes
│   └── Grimoire.Hub/
│       ├── Runtime/Paths/                    # resolution + the new log event/metric
│       ├── WikiIdentity/                     # NEW — brief, custodian, reporting
│       ├── Cli/WikiIdentityCommand.cs        # NEW — the wizard command
│       └── Cli/HubCliCommands.cs             # + catalog entry
├── Directory.Build.targets                   # delivers foundation-prompt.md per agent
└── tests/
    ├── Grimoire.ArchTests/                   # Boundary Rule literal + allow-list
    ├── Grimoire.IntegrationTests/            # composition, resolution, wizard, signals
    └── Grimoire.EvalRunner/                  # + foundation_prompt fingerprint

deploy/server/grimoire-server                 # thin glue: start the Hub wizard, show identity
docs/adr/ADR-053, ADR-054, ADR-056            # the three ADRs (055 drafted, declined)
```

**Structure Decision**: no new project and no new assembly. The composition work lands in the shared
agent runtime library both by nature and by ADR-044; the wizard lands in the Hub beside the CLI surface
it belongs to, in its own `WikiIdentity` namespace so ADR-056's allow-list has exactly one named entry
to point at.

## Constitution Re-Check (post-design)

Re-evaluated after Phase 1 with the design fixed:

- **Principle I**: still no new external system, no new port, no infrastructure package anywhere new.
  The wizard touches the filesystem, which the persistence exemption covers, and its namespace is new
  but its dependencies are not. **Pass.**
- **Principle II**: every deterministic criterion has a hermetic test against real infrastructure; the
  two lower-stakes agent-judgment criteria are explicitly *not* given an eval suite, and the plan says
  so rather than leaving it implied. Every test named asserts a product-owned contract — resolution
  outcomes, composed text, exit codes, persisted bytes, emitted signals — and none re-verifies
  Spectre.Console, the configuration binder or the filesystem. **Pass.**
- **Principle III**: three single-aspect ADRs, one Boundary Rule with a Red/Green probe in Phase 0, and
  every other rule tagged Feature-Scoped Invariant with a behavioral test. The declined fourth is the
  rule working: an ADR that fixed feature behaviour was caught in review rather than accepted. **Pass,
  gated on acceptance.**
- **Principle IV**: 2 metrics, 5 log events, 2 spans, each with its three derived task categories, and
  the span tests bound to the production composition root. **Pass.**
- **Principle V**: the harness gained custody, not authorship, and the gain is fenced by a structural
  rule rather than by intent. The one judgment call — the drafting brief — is named above with the
  test for when it has gone wrong. **Pass.**

## Complexity Tracking

No Constitution Check violations to justify.
