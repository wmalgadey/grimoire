# Implementation Plan: Explicit Path Configuration (Decouple from Repository Structure)

**Branch**: `005-content-root-config` | **Date**: 2026-07-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-content-root-config/spec.md`

## Summary

Remove all runtime dependence on the source-repository structure. A single composition
point (`GrimoirePathOptions` + `GrimoirePathResolver`, namespace
`Grimoire.Hub.Runtime.Paths`) defines and resolves every runtime location beneath the
base directory (base = configured `--base-dir`, else the process working directory)
in two homes: the wiki content root at `<base>/wiki` — deliberately separate so the
wiki can be committed to its own git repository — and all internal runtime data
beneath a consolidated `<base>/data` directory. Configuration is layered through the
standard channels with precedence
CLI > env > `appsettings.json` > defaults. `FindRepoRoot` (git shell-out) is deleted
from Hub and IngestAgent; the agent receives a new explicit `--wiki-root` argument that
anchors relative page paths and safety-policy prefixes. Startup validates fail-fast
(required inputs) / auto-creates (writable data) and reports every resolved location.
Existing checkout data moves once, manually, into `data/` — the wiki stays in place
(documented in [quickstart.md](quickstart.md)). Full rationale:
[research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (ADR-001)

**Primary Dependencies**: ASP.NET Core Minimal APIs + SignalR (Hub), Microsoft.Extensions.Configuration providers (command line, environment, JSON — already in the shared framework), Mono.Cecil + NetArchTest (arch tests), OpenTelemetry .NET SDK (ADR-005)

**Storage**: File system — wiki markdown in its own `wiki/` directory (independently git-committable) + consolidated `data/` directory (raw intake artifacts, `.env`, agent instructions) + embedded SQLite for operational state (ADR-003) — locations become configurable, engines unchanged

**Testing**: xUnit — Grimoire.ArchTests (Cecil/NetArchTest), Grimoire.IntegrationTests (hermetic, in-memory OTel exporter per ADR-005), no new Testcontainers need (no new external API boundary)

**Target Platform**: Local dev (macOS checkout) and production-style install in an arbitrary directory without git or source tree

**Project Type**: Web service (Hub) + console worker (IngestAgent) in one solution

**Performance Goals**: None new — startup path resolution is O(number of locations), one-time

**Constraints**: No new configuration mechanism; no repo/VCS dependency at runtime; prod/dev differ by configuration values only; internal layout inside each root stays system-owned

**Scale/Scope**: 8 configurable locations, 2 processes, ~6 production files touched + 1 new module + policy/gitignore/launch.json migration

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
| --- | --- | --- |
| I — Domain Architecture & Strategic DDD | Harness-only feature; no domain types touched; no tactical DDD added. Path module lives in Hub infrastructure, Domain stays dependency-free. | PASS |
| II — Pragmatic Testing | All success criteria are deterministic harness guarantees → hermetic integration + arch tests, no live LLM calls, no mocked-database repositories. No agent-judgment surface ⇒ no evaluation tests required. | PASS |
| III — ADR-Driven & Test-Enforced | All 8 existing ADRs read; new cross-cutting boundary ⇒ ADR-009 drafted (below); structural rule + Red/Green probe defined as Phase 0. ADR-009 must reach Accepted before `/speckit-tasks`. | PASS (pending ADR-009 acceptance) |
| IV — Behavioral & Observable Engineering | Observability section enumerates log events with full derivation contract; no new infrastructure introduced; CI gates listed. | PASS |
| V — Agentic Core & Deterministic Harness | No wiki-content judgment moves into backend code. Instruction files keep governing the agent's context (only their location becomes configurable). Guarded tool boundary unchanged; policy-prefix anchor change is a guardrail-rule change, which Principle V explicitly permits. | PASS |

**Post-Phase-1 re-check**: design artifacts introduce no violations — the agent-launch
contract delta ([contracts/agent-launch.md](contracts/agent-launch.md)) carries only
harness mechanics (paths), no content semantics.

## Architectural Constraints & ADRs

*GATE: All ADRs in `docs/adr/` read before completing this section.*

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-001 | Backend/Frontend Tech Stack | Stay on .NET 10 / stock ASP.NET Core configuration stack; no new runtime or config framework. |
| ADR-002 | Ingest Agent Execution Model | Child-process spawn with CLI-arg path passing must be preserved; this feature only extends the argument list (`--wiki-root`) and how the Hub computes the values. |
| ADR-003 | Domain vs. Operational State Persistence | SQLite operational state stays out of git; markdown domain state stays git-diffable. The layout keeps `data/state/` (+ `data/raw/`) ignored while the wiki lives outside `data/` as an independently version-controllable tree. ADR-009 supersedes only ADR-003's illustrative `.grimoire/` naming. |
| ADR-004 | Credential Scoping | Secrets stay in a local git-ignored file read by the Hub and injected only into the agent's environment; only the file's location becomes configurable (`<data>/.env` default). |
| ADR-005 | Observability Backend | New log events verified in CI via the in-memory OTel exporter; local via Aspire dashboard OTLP. |
| ADR-006 | Agent Tool Loop & Guarded Boundary | Deny-by-default policy file, guarded executor, and write journal unchanged; policy path-prefix *anchor* moves to the content root (guardrail-rule change), prefixes rewritten accordingly. |
| ADR-007 | Agent Instruction Surface | `system-prompt.md` / `default-user-prompt.md` / `policy.json` continue to be loaded by explicit path with fail-closed behavior and SHA-256 identity; only the directory they live in becomes configuration. |
| ADR-008 | Agent Event Channel & Run Supervision | NDJSON stdout channel, supervision, and queue untouched; dispatch continues to pass absolute Hub-resolved paths. |
| **ADR-009** | **Explicit Runtime Path Configuration and Consolidated Data Directory** (drafted by this plan, [docs/adr/ADR-009-runtime-path-configuration.md](../../docs/adr/ADR-009-runtime-path-configuration.md)) | Single composition point; two-home defaults (`<base>/wiki` + consolidated `<base>/data`); precedence order; no ambient-directory access outside `Grimoire.Hub.Runtime.Paths`; no VCS invocation; structural rule as below. |

**New ADR required?**: Yes — ADR-009 drafted; MUST be moved to **Accepted** (author
sign-off) before `/speckit-tasks` is invoked (Constitution III / workflow step 4).

## Agentic Boundary (Constitution Principle V)

No agentic surface — harness-only feature. For the record:

| Capability | Side | Where it lives |
| --- | --- | --- |
| Path resolution, validation, startup reporting | Harness | `Grimoire.Hub.Runtime.Paths` (new) |
| Passing `--wiki-root` + absolute paths to the worker | Harness | `AgentDispatch/AgentProcessHost.cs` |
| Policy prefix anchor at content root | Harness (guardrail rule) | `AgentCore/PolicyLoader.cs` + `data/agents/ingest/policy.json` |
| Wiki content decisions | Agentic core | unchanged — `data/agents/ingest/system-prompt.md` etc. keep governing the agent context |

## Test Strategy

*Every success criterion maps to its primary verification method.*

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| SC-001 start + operate in repo-less dir | Deterministic guarantee | Hermetic integration test | Fake agent launcher (existing `IAgentProcessLauncher` seam); temp directories; no git binary invoked | Prepared `data/` tree fixture (instructions + `.env` stub) | Boot the Hub host in a temp base dir without `.git`; assert successful start and that all writes land under configured roots |
| SC-002 invalid required input fails fast | Deterministic guarantee | Hermetic integration test | Temp dirs with deliberately missing secrets/instructions/worker | Per-location negative fixtures | Assert non-zero startup failure + `paths_validation_failed` naming location, configured value, resolved path |
| SC-003 zero-config resolves under `<cwd>/wiki` + `<cwd>/data` | Deterministic guarantee | Hermetic unit/integration test on `GrimoirePathResolver` | None (pure resolution) + integration variant with temp cwd | Default-options fixture | Assert content root resolves to `<cwd>/wiki` and every internal data path beneath `<cwd>/data`; doubles as the defaults-in-one-place regression test |
| SC-004 agent receives all paths, no discovery | Deterministic guarantee | Arch test + hermetic integration test | Cecil IL scan; recorded dispatch args via fake launcher | Violation probe class (Red/Green, then deleted) | `RuntimePathsBoundaryRuleTests` + assertion that every dispatch includes `--wiki-root` and absolute paths |
| SC-005 every start reports resolved locations | Deterministic guarantee | Hermetic integration test (in-memory OTel/log exporter per ADR-005) | In-memory log/exporter capture | Same fixtures as SC-001/SC-003 | Assert `paths_resolved` event with all 8 mandatory path fields |

No agent-judgment criteria exist in spec 005 ⇒ no evaluation-threshold tests (Principle
II split respected in both directions).

## Observability

### Business Metrics (OpenTelemetry Counters / Gauges)

None. This feature is one-shot startup composition: it produces no recurring
domain-meaningful quantity to count or gauge. Existing metrics are unaffected.
(Deliberate empty set — not an omission.)

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `paths_resolved` | INFO | Once per successful startup, after validation/creation, before serving | `base_dir`, `data_dir`, `content_root`, `raw_dir`, `state_db`, `secrets_file`, `instructions_dir`, `agent_worker` (resolved absolute paths) + `sources` (per-location config source) |
| `paths_location_created` | INFO | Each writable-data location auto-created at startup | `location` (logical name), `resolved_path` |
| `paths_validation_failed` | ERROR | A required input location is missing / wrong kind at startup, immediately before non-zero exit | `location`, `configured_value`, `resolved_path`, `reason` |

**Derivation rule (MANDATORY)**: each row above requires in `tasks.md`: (1)
implementation task with stable event name + mandatory fields, (2) deterministic
integration test validating name/level/fields, (3) CI task ensuring these tests run in
the standard PR pipeline.

### Distributed Trace Spans (OpenTelemetry)

None new. Path resolution completes during host startup, before any traced request or
agent-run activity exists; there is no parent to attach to and no cross-process flow to
correlate. Existing span chains (ingest run, dispatch) are unchanged and continue to
correlate via `task_id`. (Deliberate empty set — not an omission.)

## Project Structure

### Documentation (this feature)

```text
specs/005-content-root-config/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output (incl. one-time migration)
├── contracts/
│   ├── path-configuration.md
│   └── agent-launch.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Grimoire.Hub/
│   │   ├── Runtime/Paths/                    # NEW — single composition point (ADR-009)
│   │   │   ├── GrimoirePathOptions.cs        # all defaults, bound from Grimoire:Paths
│   │   │   ├── GrimoirePathResolver.cs       # resolve + validate + auto-create + report
│   │   │   └── ResolvedGrimoirePaths.cs      # injected everywhere paths are consumed
│   │   ├── appsettings.json                  # NEW section Grimoire:Paths (single config home)
│   │   ├── Program.cs                        # CHANGED — FindRepoRoot deleted; binds options
│   │   ├── ContentRoot/ContentRootPaths.cs   # CHANGED — projection of ResolvedGrimoirePaths
│   │   ├── ContentRoot/RawStoragePaths.cs    # CHANGED — same
│   │   ├── AgentDispatch/AgentProcessHost.cs # CHANGED — worker path forms + --wiki-root
│   │   └── Submission/SubmissionService.cs   # CHANGED — cwd-based source resolution
│   └── Grimoire.IngestAgent/
│       ├── Program.cs                        # CHANGED — FindRepoRoot deleted; --wiki-root
│       └── AgentCore/PolicyLoader.cs         # CHANGED — prefixes anchored at wiki root
└── tests/
    ├── Grimoire.ArchTests/RuntimePathsBoundaryRuleTests.cs   # NEW (Phase 0, Red/Green probe)
    └── Grimoire.IntegrationTests/PathConfigurationTests.cs   # NEW (SC-001/002/003/005 + log contract)

wiki/                                         # UNCHANGED location — separate from data/,
                                              #   independently git-committable
data/                                         # NEW consolidated internal-data home (one-time migration)
├── raw/  ├── state/  ├── agents/ingest/  └── .env
.gitignore                                    # CHANGED — data/state/, data/raw/
.vscode/launch.json                           # CHANGED — config-only prod/dev split
docs/adr/ADR-009-runtime-path-configuration.md  # NEW (drafted by this plan)
```

**Structure Decision**: Existing web-service + worker layout retained; the only new
module is `Grimoire.Hub.Runtime.Paths`, deliberately a single namespace so the ADR-009
structural rule can whitelist it.

## Complexity Tracking

No constitution violations to justify — table intentionally empty.
