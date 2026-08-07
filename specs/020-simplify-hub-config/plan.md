# Implementation Plan: Simplify Hub CLI Configuration

**Branch**: `020-simplify-hub-config` | **Date**: 2026-08-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/020-simplify-hub-config/spec.md`

## Summary

Collapse the hub's 16-switch path surface to exactly three directory options — runtime
data, agent, wiki — resolved per option by CLI > environment > a **mandatory** versioned
`appsettings.json` with no code-level fallback. `BaseDir` disappears in favour of three
independent roots (`.grimoire/` and `llm-wiki/` as cwd siblings, the agent folder inside
`.grimoire/`), so relocating runtime data provably leaves the wiki alone. Agent instruction
files stop being loose data under the data directory, and the **whole agent runtime** —
worker DLL, `deps.json`, every dependency assembly and the instruction documents — is
delivered into `<AgentDir>/<agent-id>/` by an MSBuild target that any operator can redirect
(`-p:GrimoireAgentDir=…`). `--agent-dir` therefore governs everything about agents, in one
directory, consumed as-is. The hub loses its ability to build one: `AgentProcessHost`'s
`.csproj` → `dotnet run --project` branch, which today can trigger a restore and compile
inside a live request, is deleted along with the bare-executable branch, leaving one launch
mode — `dotnet <AgentDir>/<agent-id>/Grimoire.<Type>Agent.dll` — and a startup failure that
tells the operator to build. Eval recordings move into the test project and eval
instruction resolution becomes repo-anchored, so eval runs no longer depend on hub
configuration or on a prior build. The secrets file moves to the project root beside
`.env-example`, outside all three roots.

**A defect uncovered while planning, and fixed by the above**: ADR-009 says the worker
default "resolves beside the Hub binaries", but `Grimoire.Hub.csproj` references only
`Grimoire.Domain` — ADR-002's `HubAgentDispatchBoundaryRuleTests` forbids a Hub→agent
assembly reference — so the agent DLLs are not in the Hub's output and no build puts them
there. That is *why* the `.csproj` launch branch existed: launch configurations pointed the
worker switch at a project file and the hub compiled the agent to substitute for an
artifact that was never delivered. SC-001 ("hub commands succeed with no flags") was
therefore unreachable. Making the agent directory the agent runtime closes the gap at its
source and satisfies FR-008's "instruction files **and runtime data**" literally.

One principle ties the agent half together: **the hub consumes build artifacts and never
produces them** — neither instruction content nor worker binaries.

The technical approach keeps every load-bearing part of ADR-009 — one options record, one
resolver, no ambient discovery outside `Grimoire.Hub.Runtime.Paths`, fail-fast validation,
auto-creation, one startup report — and replaces only the parts that produced the sprawl.
Because that touches four accepted ADRs, the change is fixed by **ADR-022** (drafted with
this plan), which additionally caps the switch count structurally so the surface cannot
regrow.

## Technical Context

**Language/Version**: C# / .NET 10 (`backend/Directory.Build.props`)

**Primary Dependencies**: `Microsoft.Extensions.Configuration` (stock providers — command
line, environment, JSON), `Spectre.Console.Cli` 0.55.0 (ADR-020, confined to
`Grimoire.Hub.Cli`), MSBuild (`Directory.Build.targets`) for agent-runtime distribution. No
new package.

**Storage**: local filesystem (directory layout) + the existing SQLite operational store,
relocated only by virtue of the data-directory root moving. No schema change.

**Testing**: xUnit. `Grimoire.ArchTests` (Mono.Cecil IL scans + NetArchTest) for the four
new structural rules; `Grimoire.IntegrationTests/PathConfiguration/` for resolution,
precedence and failure contracts; `Grimoire.AgentEvals` replay tier against relocated
fixtures. Tiering per ADR-021.

**Target Platform**: cross-platform .NET host (Linux/macOS containers and dev machines)

**Project Type**: backend service + CLI (`Grimoire.Hub`), three agent worker executables,
one eval runner console app

**Performance Goals**: N/A — startup-time path composition only; resolution stays a single
pass over ~15 locations, no measurable change.

**Constraints**: pre-1.0, no external consumers — breaking removal of 13 switches with no
aliases and no on-disk migration (FR-014). Hermetic tests only: no live LLM calls, no real
credentials (Principle II).

**Scale/Scope**: one configuration surface; ~25 Hub files touched by the
`ContentRoot`→`WikiDir` rename, ~8 integration-test files touched by the
`ResolvedGrimoirePaths` restructuring, 3 agent projects gaining `Instructions/` sources and
a `GrimoireAgentId`, one new `Directory.Build.targets`, one localized deletion in
`AgentProcessHost` (5 spawn sites), 259 git-tracked recording files relocated,
`.gitignore`, CI workflows, launch configurations, and the ADR-002/007/009/012/019/020
documentation touched by ADR-022.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
| --- | --- | --- |
| **I — Domain architecture & hexagonal boundaries** | PASS | No new external system, so no new port is required. The filesystem is a local-filesystem/persistence concern, port-exempt by the constitution's own persistence exemption — `GrimoirePathResolver` keeps talking to `System.IO` directly. Adapter containment (C1–C9) is untouched: no infrastructure package moves namespace. Ubiquitous Language is improved, not weakened (`ContentRoot` → `WikiDir`, research R9). |
| **II — Pragmatic testing** | PASS | Every criterion is a deterministic harness guarantee; the spec correctly declares no agent-judgment criteria (this feature changes configuration, not judgment). Tests are state-based against the real filesystem in temp directories, plus process-spawn tests for the no-flags start. No mocking framework, no double introduced to isolate an internal collaborator (research R11). |
| **III — ADR-driven & test-enforced** | PASS | All ADRs in `docs/adr/` were read. The change supersedes load-bearing parts of ADR-009 and amends ADR-002/007/012/019/020, so **ADR-022 is drafted with this plan** and must reach Accepted before `/speckit-tasks`. Four new rules (R1–R4), each with a Red/Green probe, land in Phase 0 before feature code. |
| **IV — Behavioral & observable** | PASS | Observability section below enumerates one metric, four log events and three spans, each deriving implementation + deterministic integration test + CI enforcement tasks. No new infrastructure — stock configuration providers and MSBuild only. |
| **V — Agentic core & deterministic harness** | PASS | Harness-only feature *with one Principle V-sensitive edge*: instruction files change home. They remain instruction files, authored in agent sources and loaded into the agent's context unchanged; the hub gains no authoring capability, and rule R3 structurally forbids it from writing instruction content. No wiki-content judgment is added, moved, or reimplemented. |

**Post-design re-check (after Phase 1, revised 2026-08-07)**: PASS — no gate changed. The
design added no port, no assembly, no infrastructure, and no backend judgment. Two items
are worth naming rather than waving through:

- The re-anchoring of conversations/findings/remediation-tasks under the wiki directory
  (clarification 2026-08-06) changes *where harness-written records live*, not *who decides
  their content*, so Principle V is unaffected; it is recorded as a deliberate consequence
  in ADR-022 because it reverses ADR-003/ADR-009's git-ignored placement.
- Removing the hub's build capability (author directive 2026-08-07a) **strengthens**
  Principle V's deterministic-harness half: the harness's job is dispatch and lifecycle,
  and compiling an agent mid-request is neither. It touches ADR-002's execution model only
  in mechanism, not in contract, and it removes a `System.Diagnostics.Process` usage rather
  than adding one — so ADR-010's C4 containment is unaffected.
- Moving the agent assemblies into the agent directory (author directive 2026-08-07b)
  **preserves** ADR-002's dispatch-only boundary rather than straining it: the Hub still
  holds no assembly reference to any agent, and the directory is filled by each agent's own
  build. Principle I is unaffected — no port, no namespace move, no infrastructure package
  crossing a boundary; the change is a build-output destination and a resolved path.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| **ADR-022** *(new, drafted with this plan — **Accepted**)* | Minimal Directory Configuration Surface — Three Roots, Mandatory Configuration File, and Build-Distributed Agent Artifacts | **Defines this feature.** Fixes the three roots and their anchors, removes the code-default tier, makes the agent directory the complete agent runtime delivered by the agent build, removes the hub's ability to build an agent (one launch mode, `dotnet <AgentDir>/<agent-id>/<worker>.dll`), moves the secrets file to the project root, relocates eval recordings, and caps the switch surface at three (rules R1–R4). Supersedes the listed parts of ADR-009; amends ADR-002/007/012/019/020. |
| ADR-009 | Explicit Runtime Path Configuration and Consolidated Data Directory | Retained in substance: one options record + one resolver as the single composition point; no ambient process-context reads outside `Grimoire.Hub.Runtime.Paths`; no `rev-parse`/`--show-toplevel` literals; fail-fast validation naming location + configured value + resolved path; auto-creation of writable locations; one startup report. `RuntimePathsBoundaryRuleTests` stays green unchanged. **Superseded parts** (`BaseDir`, two-homes-beneath-a-base, "> code defaults", per-location switches, `<base>/data/.env`, `<base>/data/agents/`, the `.csproj`/`.dll`/executable worker launch modes and the "launch configurations must pass the agent-worker path when running from source" consequence) are enumerated in ADR-022. |
| ADR-002 | Ingest Agent Execution Model | The child-process spawn model, the file-based contract, and the per-spawn argument set are **unchanged**, and its `HubAgentDispatchBoundaryRuleTests` no-assembly-reference rule is preserved — the agent directory is filled by each agent's *own* build, never by the Hub's. That rule is **newly load-bearing**: it is what makes the agent directory the only possible home for the agent runtime. Only the launch mechanism narrows from three modes to one (`dotnet <AgentDir>/<agent-id>/<worker>.dll`). |
| ADR-008 | Agent Event Channel & Run Supervision | Unchanged, and better served: supervision and heartbeat timings were written assuming process *start*; removing the build-on-dispatch path restores that assumption. |
| ADR-020 | Hub CLI Command Surface | `PathSwitchCatalog` remains the single declaration point for path switches; `HubPathSettings` keeps 1:1 parity with it and the help provider keeps generating "Server options:" from it. Only the catalog's size changes (16 → 3). Exit codes, dispatch, blocking execution, and `lint.pid` behavior are untouched — `lint.pid` stays a fixed filename under the data directory. |
| ADR-007 | Agent Instruction Surface | The document set per agent (`system-prompt.md`, `policy.json`, Ingest's `default-user-prompt.md`), fail-closed loading, and SHA-256 traceability are unchanged. Only the files' home moves: agent-project `Instructions/` sources → `<AgentDir>/<agent>/` by build. |
| ADR-006 | Agent Tool Loop & Guarded Boundary | `policy.json` travels with the other instruction files; its content, its content-root-relative prefix anchoring, and the guarded-tool enforcement are untouched. |
| ADR-012 | Standalone Eval Runner and Recorded Replay | Recording **format**, fingerprint semantics, and the staleness merge gate are unchanged. The store's **location** moves to `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/` and `--recordings-root` is removed. Instruction fingerprints are computed over the agent-project sources, which are the same bytes the agents load. |
| ADR-019 | Devcontainer Host Runtime and Credential Access | Amended: the secrets file it exposes becomes `<repo-root>/.env` instead of `<base>/data/.env`. No new credential mechanism; `.env-example` remains the declared variable surface. |
| ADR-004 | Credential Scoping | Unchanged — per-spawn injection of the secret into the specific child process. Only the file's path changes. |
| ADR-003 | Domain / Operational-State Persistence | The SQLite store moves with the data root; single-writer semantics, WAL, and busy-timeout are untouched. **Note**: conversations/findings/remediation-tasks re-anchor to the wiki directory, reversing their git-ignored placement — recorded in ADR-022's consequences. |
| ADR-010 / ADR-011 / ADR-013 | Hexagonal namespaces, shared agent runtime, agent packaging & naming | Containment rules C1–C9 and the N1 ownership map are untouched: no infrastructure package changes namespace and no new agent artifact is introduced. The agent CLI contract (`--wiki-root` and friends) is unchanged. C4 (process spawning confined to `…Adapters.AgentProcess` + MarkItDown) keeps its scope — new rule R4 narrows *what* that adapter may spawn, it does not move the boundary. |
| ADR-021 | Test Tier Taxonomy | New arch rules join the fast tier; new path tests join the integration tier; relocated recordings keep the replay tier hermetic. No fixed unconditional waits introduced. |

**New ADR required?**: **Yes — `docs/adr/ADR-022-minimal-directory-configuration-surface.md` is
drafted (status: proposed) and MUST reach Accepted before `/speckit-tasks` is invoked.**

## Agentic Boundary (Constitution Principle V)

Harness-only feature, with one boundary-sensitive move that is called out explicitly rather
than waved through.

| Capability | Side | Where it lives |
| --- | --- | --- |
| All wiki-content judgment (page existence, update-vs-create, supersession, categorization, confidence, tagging, index/log content) | Agentic core | `backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/system-prompt.md` — **relocated, not rewritten**; byte-identical content, same fail-closed loading into the agent's context |
| Guardrail policy | Agentic core boundary | `backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/policy.json` — relocated, unchanged |
| Directory-option resolution and precedence | Harness | `Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs` |
| Mandatory-configuration and agent-directory validation | Harness | `Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs` |
| CLI switch surface and help generation | Harness | `Grimoire.Hub/Runtime/Paths/PathSwitchCatalog.cs`, `Grimoire.Hub/Cli/` |
| Instruction-file distribution to the agent directory | Harness (build) | `backend/Directory.Build.targets` — copies files; never generates or edits their content |
| Eval fixture and instruction resolution | Harness (test tooling) | `Grimoire.EvalRunner/Workspace/EvalPaths.cs` |

**Boundary check**: no wiki-maintenance behavior changes, so no instruction content changes.
The hub gains no ability to author instructions — structural rule R3 forbids any production
type outside `Grimoire.Hub.Runtime.Paths` from using an instruction filename as a write
target, with a Red/Green probe. Instruction files continue to be loaded into the agent's
working context by `AgentHost` exactly as before; only their path changes.

## Test Strategy

*MANDATORY: Every success criterion in spec.md maps to its primary verification method.*

All eleven criteria are deterministic harness guarantees. The spec declares no
agent-judgment criteria, correctly: this feature changes where files live, not what any
agent decides. No evaluation-threshold rows apply.

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| **SC-001** Hub commands succeed with no flags and no environment variables | Deterministic guarantee | Hermetic integration test, process spawn | None — real Hub binary, no LLM (command exits before any agent spawn) | Temp directory seeded with `.env` and an agent directory produced by a real agent build (`-p:GrimoireAgentDir=<temp>`), plus the shipped `appsettings.json` | Reuses the `HubHelpUsageTests` spawn idiom (cwd = temp dir, empty argv); asserts exit code 0 and a `paths_resolved` line whose sources are all `config-file`. **This criterion was unreachable before the agent-runtime relocation** — nothing put worker DLLs at the old default location, so the test doubles as the regression guard for that defect |
| **SC-002** Exactly 3 directory options (down from 16) | Deterministic guarantee | Architecture test (rule R1) | None | None | `PathSwitchCatalog.All` count + exact names, and `HubPathSettings` `[CommandOption]` 1:1 parity; Red/Green probe adds a fourth entry |
| **SC-003** 100% of runtime artifacts under the data root | Deterministic guarantee | Hermetic integration test | None — real filesystem in a temp directory | Options record with a custom `DataDir` | Asserts `RawOriginalsDir`, `RawSourcesDir`, `StateDbPath`, `WriteLocksDir`, `LintPidPath`, `AgentDir` are all under the configured root — enumerated from `ResolvedGrimoirePaths`, so a newly added location cannot silently escape |
| **SC-004** 100% of agent-result artifacts under the wiki root, regardless of the data root | Deterministic guarantee | Hermetic integration test | None | Options with `WikiDir` and `DataDir` pointed at *different* temp roots | Asserts `IndexPath`, `LogPath`, `TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir` are under the wiki root and none is under the data root (US2 AS2 / US3 AS2 in one assertion) |
| **SC-005** Per-option precedence CLI > env > file | Deterministic guarantee | Hermetic integration test | None — real `ConfigurationBuilder` with all three providers | Matrix over the 3 options × 3 tiers, incl. mixed cases (one option from CLI, another from env) | Extends `PathConfiguration/PathPrecedenceTests`; asserts both the resolved value and `PathLocation.Source` |
| **SC-006** Missing/empty/incomplete config file fails naming the file | Deterministic guarantee | Hermetic integration test | None | Config roots built from `{}`, from a section missing one root, and from a whitespace value | Asserts `GrimoirePathValidationException` message names `appsettings.json` and the missing key(s), and that **no** directory was created before the throw |
| **SC-007** Missing/empty agent directory fails naming that directory | Deterministic guarantee | Hermetic integration test | None | Temp dirs: absent agent dir; present-but-empty agent dir; agent dir missing one required document | Three distinct assertions — the empty-directory case must name `agent_dir`, not an individual file |
| **SC-008** Every agent build leaves the target agent directory holding that agent's current runtime | Deterministic guarantee | Build-contract integration test | None — invokes `dotnet build` with `-p:GrimoireAgentDir=<temp>` | Agent project sources + their build output | Asserts each agent subfolder holds its worker DLL, `deps.json`, `runtimeconfig.json` and `Instructions/` with the required documents, byte-matching the sources; a second build after touching a source refreshes the copy (FR-011); a planted stale file is **gone** after the next build (clear-then-copy); and the delivered directory is genuinely launchable — spawn `dotnet <temp>/ingest/Grimoire.IngestAgent.dll --help` and assert it starts rather than failing assembly resolution |
| **SC-009** Eval recordings resolve from the fixture location; no recordings path on the CLI | Deterministic guarantee | Hermetic integration test + architecture-style assertion | None | Relocated `Fixtures/recordings/` tree | Asserts `EvalPaths.RecordingsRoot` resolves under the test project and that the eval runner's parser rejects `--recordings-root`; the existing replay suite passing against the new location is the second half of the proof |
| **SC-010** Eval runs identical regardless of hub configuration, with no agent build | Deterministic guarantee | Hermetic replay-eval run (ADR-021 replay tier) | Recorded LLM turns via `ReplayModelClient` (existing port fake path — no live calls) | Existing recordings + scenario fixtures | Runs the replay suite with `Grimoire__Paths__*` set to junk paths and with the agent directory deleted; asserts identical sample counts and scores |
| **SC-011** Secrets resolve to the project root regardless of directory options | Deterministic guarantee | Hermetic integration test | None | Options with all three roots pointed at temp locations | Asserts `SecretsFilePath` == `<cwd>/.env` in every case, for both `GrimoirePathResolver` and `EvalPaths.LocalEnvPath` |
| **Directive 2026-08-07 (a)** The hub never builds an agent; one launch mode; missing worker fails at startup | Deterministic guarantee | Architecture test (rule R4) + hermetic integration test | None | Temp agent directory with one worker DLL removed | R4 proves no build-invocation literal survives anywhere in production code; the integration test proves the missing worker fails during `GrimoirePathResolver.Resolve` (not at dispatch) with a reason containing `Build first: dotnet build backend/Grimoire.slnx`. **No spec SC covers this yet — see the note below the table.** |
| **Directive 2026-08-07 (b)** The agent runtime lives entirely in the agent directory | Deterministic guarantee | Hermetic integration test | None | Options with `AgentDir` pointed at a temp location | Asserts every agent's worker path and instruction paths resolve under the configured `AgentDir` and that **no** resolved path anchors at `ProcessBaseDirectory`; covered end-to-end by the SC-008 launchability assertion above. Partially backed by spec FR-008 ("instruction files and runtime data … as per-agent-type subfolders") — the worker-binary half still needs the FR-020 wording below |

> **Spec backing for the 2026-08-07 directives — closed.** `spec.md` was amended alongside
> this revision so no plan requirement lacks a spec source: **FR-020** (launch from pre-built
> artifacts only; never build), **FR-021** (the agent build delivers each agent's complete,
> self-contained runtime into its subfolder), **SC-012** (100% of launches start a pre-built
> artifact; 0% of hub code paths can invoke a build tool). FR-013/SC-007/SC-008 were widened
> from "instruction files" to "agent runtime", and the Assumption that workers "resolve
> relative to the hub's own binaries" was corrected — planning established it is not
> implementable, because the hub holds no assembly reference to any agent (ADR-002) and so
> its own output never contains them.

**Structural rules (Phase 0, before feature code — each with a Red/Green probe)**

| Rule | Test | Probe |
| --- | --- | --- |
| R1 — switch surface capped at three, `HubPathSettings` parity | `Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests` | add a fourth catalog entry ⇒ red ⇒ remove |
| R2 — no code-level path defaults (`.grimoire`, `llm-wiki` never appear as production IL literals) | `Grimoire.ArchTests/NoCodeLevelPathDefaultsRuleTests` | reintroduce a `DefaultDataDirName` constant ⇒ red ⇒ remove |
| R3 — no production type outside `Grimoire.Hub.Runtime.Paths` writes instruction filenames | `Grimoire.ArchTests/InstructionAuthorshipBoundaryRuleTests` | a Hub type writing `system-prompt.md` ⇒ red ⇒ remove |
| R4 — no production assembly carries build-invocation IL literals (`.csproj`, `--project`, `msbuild`, `dotnet build`/`restore` arguments), with a documented allow-list for the eval runner's diagnostic *message* strings | `Grimoire.ArchTests/NoRuntimeBuildInvocationRuleTests` | restore the `.csproj` → `dotnet run --project` branch in `AgentProcessHost` ⇒ red ⇒ remove |

**Doubles inventory**: none introduced. The only test double involved is the pre-existing
`ReplayModelClient` at the `IModelClient` port (ADR-012), used unchanged. No mocking
framework is referenced (Principle II).

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
| --- | --- | --- | --- |
| `grimoire.hub.path_resolution_failures_total` | Counter | Startup path-composition failures, incremented immediately before the process exits non-zero. Lets an operator distinguish "misconfigured" from "crashed" without reading logs. | `reason` = `configuration_missing` \| `agent_directory_empty` \| `location_invalid` |

No counter is declared for successful resolution: it happens exactly once per process
start and is already fully described by the `paths_resolved` event, so a counter would add
cardinality without adding signal.

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `paths_resolved` | INFO | Once per successful start, after validation and auto-creation, before serving or before a CLI command runs | `data_dir`, `wiki_dir`, `agent_dir`, `secrets_file`, `state_db`, `raw_dir`, `sources` |
| `paths_location_created` | INFO | Each writable data location auto-created at startup | `location`, `resolved_path` |
| `paths_configuration_missing` | ERROR | **New** — a required root is absent from every configuration tier, immediately before non-zero exit | `configuration_file`, `missing_keys` |
| `paths_validation_failed` | ERROR | A required input location is missing or of the wrong kind, immediately before non-zero exit | `location`, `configured_value`, `resolved_path`, `reason` |

`paths_resolved`'s field set changes with this feature: `base_dir` and `content_root` are
removed, `wiki_dir` and `agent_dir` are added. `paths_location_created` and
`paths_validation_failed` keep their existing field contracts; `paths_validation_failed`
gains `agent_dir` as a possible `location` value with the distinct reason
`agent directory contains no instruction files`.

**Derivation rule (MANDATORY)**: Every row above MUST map to concrete work in `tasks.md`
covering all three categories:

1. Implementation task(s) with stable event name and mandatory fields —
   `GrimoirePathLogEvents` gains `LogConfigurationMissing` and updates
   `LogPathsResolved`'s fields.
2. Deterministic integration test task(s) validating event name, level, and mandatory
   fields — extending `PathConfiguration/PathLoggingContractTests`, which already asserts
   exactly this shape for the existing three events.
3. CI task(s) ensuring those logging tests run in the standard PR pipeline — they live in
   `Grimoire.IntegrationTests`, already run by `.github/workflows/ci.yml`; the task
   verifies the job still covers them after the file moves.

### Distributed Trace Spans (OpenTelemetry)

Following the established `GrimoirePathLogEvents` pattern: each path log event starts a
matching `HubTracing.ActivitySource` span tagged `signal_type=log`, so logs and traces
correlate through the same event name and share the startup activity context.

| Span name | Parent span | Attributes |
| --- | --- | --- |
| `paths_resolved` | root (startup, no parent activity exists yet) | `signal_type=log`, `event_name=paths_resolved`, `level=Information`, `data_dir`, `wiki_dir`, `agent_dir`, `secrets_file`, `state_db`, `raw_dir`, `sources` |
| `paths_configuration_missing` | root | `signal_type=log`, `event_name=paths_configuration_missing`, `level=Error`, `configuration_file`, `missing_keys` |
| `paths_validation_failed` | root | `signal_type=log`, `event_name=paths_validation_failed`, `level=Error`, `location`, `configured_value`, `resolved_path`, `reason` |

`paths_location_created` keeps its existing span, unchanged in name and attributes.

**Derivation rule (MANDATORY)**: Every row above MUST map to concrete work in `tasks.md`
covering all three categories:

1. Implementation task(s) creating the span with the declared attributes — in
   `GrimoirePathLogEvents.StartLogEventSpan`, the existing mechanism.
2. Deterministic integration test task(s) validating span name, parent/child relationship
   (root at startup) and correlation attributes — an in-memory `ActivityListener`
   assertion alongside the log-contract tests, matching the existing idiom.
3. CI task(s) ensuring those trace tests run in the standard PR pipeline — same
   `Grimoire.IntegrationTests` job as above.

## Project Structure

### Documentation (this feature)

```text
specs/020-simplify-hub-config/
├── plan.md                              # This file (/speckit-plan output)
├── spec.md                              # Feature specification
├── research.md                          # Phase 0 output — R1..R12
├── data-model.md                        # Phase 1 output — configuration composition
├── quickstart.md                        # Phase 1 output — runnable validation scenarios
├── contracts/                           # Phase 1 output
│   ├── directory-options.md             #   the three switches, precedence, failures
│   ├── appsettings-paths.md             #   the mandatory Grimoire:Paths section
│   └── agent-instruction-build.md       #   GrimoireAgentId/GrimoireAgentDir build contract
└── tasks.md                             # Phase 2 output (/speckit-tasks — NOT created here)

docs/adr/
└── ADR-022-minimal-directory-configuration-surface.md   # drafted with this plan (proposed)
```

### Source Code (repository root)

```text
backend/
├── Directory.Build.props
├── Directory.Build.targets                        # NEW — PublishAgentRuntime target (clear + copy $(OutDir)** → agent dir)
├── src/
│   ├── Grimoire.Hub/
│   │   ├── appsettings.json                       # MODIFIED — mandatory, all defaults
│   │   ├── HubHostComposition.cs                  # MODIFIED — mandatory-config check
│   │   ├── Runtime/Paths/
│   │   │   ├── GrimoirePathOptions.cs             # MODIFIED — 3 roots + sub-paths, no Default* constants
│   │   │   ├── GrimoirePathResolver.cs            # MODIFIED — anchors, mandatory-root + agent-dir validation
│   │   │   ├── ResolvedGrimoirePaths.cs           # MODIFIED — WikiDir, AgentDir, AgentInstructionPaths
│   │   │   ├── PathSwitchCatalog.cs               # MODIFIED — 16 → 3 entries
│   │   │   └── GrimoirePathLogEvents.cs           # MODIFIED — new event + changed fields
│   │   ├── Cli/HubPathSettings.cs                 # MODIFIED — 16 → 3 options
│   │   ├── AgentDispatch/Adapters/AgentProcess/
│   │   │   └── AgentProcessHost.cs                # MODIFIED — one launch mode; .csproj/exe branches deleted at all 5 spawn sites
│   │   └── ContentRoot/{ContentRootPaths,RawStoragePaths}.cs   # MODIFIED — project from WikiDir (type names unchanged, research R9)
│   ├── Grimoire.IngestAgent/
│   │   ├── Instructions/                          # NEW — moved from data/agents/ingest/
│   │   └── Grimoire.IngestAgent.csproj            # MODIFIED — GrimoireAgentId + Content/CopyToOutputDirectory
│   ├── Grimoire.QueryAgent/{Instructions/,*.csproj}   # NEW + MODIFIED — same shape
│   ├── Grimoire.LintAgent/{Instructions/,*.csproj}    # NEW + MODIFIED — same shape
│   └── Grimoire.EvalRunner/
│       ├── Program.cs                             # MODIFIED — drop --recordings-root
│       └── Workspace/EvalPaths.cs                 # MODIFIED — sources, fixtures, repo-root .env
└── tests/
    ├── Grimoire.ArchTests/                        # NEW — R1, R2, R3, R4 rule tests
    ├── Grimoire.IntegrationTests/PathConfiguration/  # MODIFIED + NEW — resolution, precedence, failures, logging/tracing
    └── Grimoire.AgentEvals/Fixtures/recordings/   # NEW LOCATION — moved from data/evals/recordings/

.env-example                                        # unchanged, now beside the real .env
.gitignore                                          # MODIFIED — .grimoire/, drop stale data/* and root tasks//conversations//remediation-tasks/
.github/workflows/{ci,eval}.yml                     # MODIFIED — recordings path globs
.vscode/launch.json, .devcontainer/                 # MODIFIED — removed switches, .env location (ADR-019 amendment)
```

**Structure Decision**: the existing backend layout is kept as-is — this feature adds no
project, no assembly and no namespace. Three new things enter the tree: `Instructions/`
folders inside the three agent projects (making instruction files agent sources), a
`backend/Directory.Build.targets` carrying the one shared MSBuild target, and a
`Fixtures/recordings/` folder in `Grimoire.AgentEvals`. Everything else is modification in
place, concentrated in `Grimoire.Hub/Runtime/Paths/` — which is exactly what ADR-009's
single-composition-point rule was written to guarantee — plus one localized deletion in
`AgentProcessHost` that removes code rather than adding it.

## Complexity Tracking

> No Constitution Check violations. Nothing to justify.

The one judgment call worth recording is a deliberate *scope* limit rather than a
violation: `ContentRootPaths` / `RawStoragePaths` keep their names while the configuration
surface and `ResolvedGrimoirePaths` adopt `WikiDir` (research R9). They are internal
projection types with no operator-facing surface; renaming them would widen an already
large change without improving the Ubiquitous Language where it is actually read — the CLI,
the configuration file, and the startup report. Recorded as a follow-up, not scheduled.
