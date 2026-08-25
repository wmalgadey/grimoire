# Implementation Plan: Independent Memory Directory Root

**Branch**: `022-memory-directory-root` | **Date**: 2026-08-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-memory-directory-root/spec.md`

## Summary

Introduce `MemoryDir` as a fourth independently configurable root in the Hub's path
surface — a cwd-anchored sibling of `DataDir`, `WikiDir` and `AgentDir`, defaulting to
`memory` — and re-anchor the four agent-bookkeeping sub-paths (`TasksDir`,
`ConversationsDir`, `FindingsDir`, `RemediationTasksDir`) beneath it instead of beneath
`WikiDir`. The wiki directory goes back to holding wiki content and nothing else, giving
an operator one location to name in a backup, retention, or exclusion rule.

The change is deliberately narrow in the resolver — one new options field, one new switch,
four changed anchor arguments — but it has three consequences that dominate the work:
ADR-022's structural three-switch cap must be amended by ADR
([ADR-024](../../docs/adr/ADR-024-memory-directory-root.md)); the wiki-relative
`Task: [[tasks/<task_id>.md]]` link written into `log.md` becomes dangling and must be
replaced with a bare task-id reference that keeps the harness log-dedup working; and the
three agent system prompts must stop describing folders the agent can no longer reach,
which invalidates all 22 eval scenarios and forces a full recording re-capture before
merge.

A fourth workstream comes from the author directive of 2026-08-11 (see the scope note
below): `Grimoire:Paths` is regrouped from a flat list of eleven keys into four anchor
groups — `Data`, `Wiki`, `Agent`, `Memory` — plus the ungrouped `SecretsFile`, so the
configuration file's tree *is* the anchoring graph. Research is in
[research.md](./research.md).

### Scope note — author directive, now specified

The configuration-file regrouping (research R8, ADR-024 option C-A) arrived as an author
directive **after** `/speckit-clarify` ran. `spec.md` has been amended to carry it, so the
plan no longer holds scope that no requirement covers:

- **FR-013** — the configuration file must express each location's anchoring root through
  its own structure, readable without consulting code. Verified by **SC-009** (100% of
  sub-paths resolve against the root they are grouped with), which is precisely what
  ADR-024 rule M5 tests.

An earlier draft paired FR-013 with a companion requirement (FR-014/SC-010) mandating
startup detection of superseded configuration keys, on the reasoning that an unrecognized
CLI switch is a parser error while an unrecognized configuration key is simply ignored —
so an operator still exporting `Grimoire__Paths__DataDir` would get the default with no
signal. That companion requirement was withdrawn on 2026-08-11 (author directive): the
project is pre-1.0 with no external installations carrying old key names, so there is no
superseded configuration to detect, and the guard's fixed eleven-entry table would be
compatibility ballast with nothing to protect. An operator still exporting a pre-regrouping
key name gets the ordinary silent-ignore treatment configuration systems give any
unrecognized key — accepted as a pre-1.0 breaking-change consequence, the same treatment
ADR-022 gave its own layout change.

The regrouping is materially in scope for this feature rather than a follow-up: it touches
the same file, the same options type, the same resolver and the same ~8 test files as the
memory root itself, and splitting it would mean editing all of them twice. It does reach
beyond the memory root — every configuration key and environment variable is renamed,
including the three existing roots' — which the spec now records as an explicit, bounded
exception to its "the other three roots are out of scope" assumption.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), nullable enabled

**Primary Dependencies**: `Microsoft.Extensions.Configuration` (layered binding, the
precedence chain), `Microsoft.Extensions.Hosting`, Spectre.Console.Cli 0.55.0 (the
`HubPathSettings` command-option surface), OpenTelemetry (existing
`paths_resolved` log/span/metric contract), Mono.Cecil (IL scanning in
`Grimoire.ArchTests`), xUnit

**Storage**: Filesystem — markdown record files under the new memory root; no schema, no
database change. The SQLite operational store (`DataDir`) is untouched.

**Testing**: The three ADR-021 tiers. Fast (`scripts/test-fast.sh`:
`Grimoire.Domain.UnitTests` + `Grimoire.ArchTests` + `Grimoire.AgentEvals --filter
"Tier=Fast"`), Integration (`Grimoire.IntegrationTests`, the project *is* the tier),
SlowEval (`Grimoire.AgentEvals --filter "Tier=SlowEval"` — but note PR CI runs the
AgentEvals project unfiltered, so SlowEval gates every PR). Deterministic waits via
`TestSupport.PollAsync` only.

**Target Platform**: Cross-platform .NET host (Linux CI, macOS/Linux dev, devcontainer)

**Project Type**: Backend service + CLI (`Grimoire.Hub`) with spawned agent worker
processes; a SvelteKit frontend that this feature does not touch

**Performance Goals**: None specific. Path resolution runs once per process start; the
feature adds one options field and one `Path.GetFullPath` call to that path.

**Constraints**: No migration of existing on-disk records (FR-011). No code-level default
for the new root (FR-006) — `appsettings.json` is the sole source, and the grouped shape
must not smuggle one in via a group-property initializer. The internal per-record layout of
all four bookkeeping directories is frozen (FR-010). Breaking configuration change is
acceptable (pre-1.0, ADR-022 precedent) — but note the key rename fails *silently*, unlike
a removed CLI switch. CLI switch names, `PathLocation` names and log field names do not
change.

**Scale/Scope**: ~12 production files in `Grimoire.Hub` and `Grimoire.EvalRunner`, 3 agent
instruction files, 5 architecture/invariant tests (1 updated, 1 extended, 3 new), roughly
30 integration-test files that construct these paths through shared fixtures (of which ~8
also reference configuration keys by string), and 252 eval recording files to re-capture.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Constitution v1.8.0 (in force at authoring time; the feature's `/speckit-plan` runs after
the 2026-08-09 amendment, so v1.8.0 binds in full).

**Correction (2026-08-12, convergence task T068)**: the version reference above was
stale even at authoring time. Commit `196f723` amended the constitution to **v1.11.0**
(Feature-Scoped Invariants vs. Boundary Rules; the Principle V instruction-content
carve-out) at 2026-08-11T20:09:43Z — before this plan's `/speckit-plan` run
(`a0f6e23`/`ec59a79`, 2026-08-12T06:05:57Z). Per Governance's non-retroactivity clause
("An amendment binds every feature whose `/speckit-plan` runs after the amendment
date"), v1.11.0 binds this feature in full, including the two gates below that were
missing from the original table. The "III — new ADR drafted" and "III — Phase 0
structural test first" rows below were written against v1.8.0's undivided
structural-test category and did not evaluate the v1.11.0 classification requirement —
see the two added rows and the Structural Enforcement section of
[ADR-024](../../docs/adr/ADR-024-memory-directory-root.md) (the classification was
originally drafted as an amending ADR-026, then merged into ADR-024 itself — see that
document's history note — since both were authored and superseded entirely within this
same unmerged branch).

| Gate | Status | Evidence |
| --- | --- | --- |
| **I — DDD & hexagonal boundaries** | PASS | No new external system, so no new port is required. The four record stores (`ConversationRecordStore`, `FindingsReportStore`, `RemediationTaskRecordStore`, `KanbanBoardProjectionStore`) are persistence/local-filesystem adapters under the Principle I exemption and stay concrete classes — they change only which resolved path they are handed. Path composition stays confined to `Grimoire.Hub.Runtime.Paths` (ADR-009 rule, `RuntimePathsBoundaryRuleTests`). No infrastructure package moves namespace. |
| **I — new boundary via ADR** | PASS | A fourth root is a change to a structurally-enforced surface, so [ADR-024](../../docs/adr/ADR-024-memory-directory-root.md) is drafted as part of this plan and MUST reach Accepted before `/speckit-tasks`. |
| **II — real infrastructure, no mocks** | PASS | Every criterion is verified against the real filesystem in per-test temp directories and the real configuration binder. No containerized dependency is involved, so no Testcontainers (2026-08-09 amendment). No mocking framework is referenced by any test project and none is added. |
| **II — classicist / state-based** | PASS | All assertions are state-based: resolved path values, files present on disk under the configured root, emitted log fields, thrown exception contents. No interaction verification. The only doubles in play are the existing hand-rolled port fakes (`FakeModelClient`, `FakeAgentProcess`) whose use is unchanged. |
| **II — harness vs. agent split** | PASS | Path resolution, anchoring, validation, auto-creation and reporting are harness contracts, tested deterministically and hermetically with no live LLM calls. The one agentic surface (FR-012 instruction edits) is verified by the existing evaluation tier, not by hermetic tests — see Test Strategy. |
| **II — success-criteria split** | PASS | The spec states all eight criteria as deterministic 100%/0% guarantees and explains why no evaluation threshold applies: the feature adds no agent judgment. FR-012 *removes* instruction text rather than adding judgment; the existing scenario thresholds serve as the regression check that the removal was harmless. No 100% guarantee is attached to an agent-judgment outcome. |
| **III — ADRs read before plan** | PASS | All 22 ADRs in `docs/adr/` were read. Constraining ADRs are listed below. |
| **III — new ADR drafted** | PASS (action required) | ADR-024 drafted at `docs/adr/ADR-024-memory-directory-root.md`, status `proposed`. **Blocks `/speckit-tasks` until Accepted.** |
| **III — Phase 0 structural test first** | PASS | Three structural rules (M1, M2, M3) with Red/Green probes, all in `Grimoire.ArchTests`, ordered first in the task list. |
| **III — Boundary Rule / Feature-Scoped Invariant classification** *(v1.11.0; added 2026-08-12, T068)* | PASS (post-hoc) | ADR-024's rules were not classified at authoring time — a standing gap closed in [ADR-024](../../docs/adr/ADR-024-memory-directory-root.md)'s Structural Enforcement section: M1/M2/M4 are Feature-Scoped Invariants with a recorded justification (Test Strategy section, above) for staying reflection-based; M3 is a Boundary Rule; M5 is a Feature-Scoped Invariant already covered by a classicist behavioral test. |
| **V — instruction-file content is eval-only, never deterministic** *(v1.11.0; added 2026-08-12, T068)* | PASS (post-hoc) | `InstructionFilesWikiScopeTests` — a deterministic test string-matching forbidden content in real `system-prompt.md` files — violated this gate and has been removed (T066). SC-008's guarantee is now corroborated only by ADR-024 rule M3 (wikilink form) and the FR-002/FR-012 evaluation thresholds; see spec.md's Assumptions section. |
| **IV — CI gate for every rule** | PASS | `Grimoire.ArchTests` and `Grimoire.IntegrationTests` both already run in the standard PR pipeline (`.github/workflows/ci.yml`); the new and updated rules inherit that gate with no workflow change. The observability contract tests likewise run there today. |
| **IV — Observability section** | PASS | Enumerated below. This feature adds no new signal; it widens the mandatory-field set of one existing log event and one existing span, and widens the trigger set of two more. Each widened row carries implementation, deterministic-test, and CI rows. |
| **IV — no unapproved infrastructure** | PASS | No cloud resource, broker, store or cache. One new local directory, which is what the feature *is*. |
| **V — agentic boundary** | PASS | No wiki-content judgment moves into backend code. The backend gains only path composition. The instruction-file edits stay instruction-file edits, which is the boundary smell test passing: this feature changes *where the harness writes its own records*, and the only agent-facing change is deleting guidance about a tree the agent can no longer see. |
| **V — guarded-tool boundary** | PASS | No change to `GuardedToolExecutor`, the policies, or the write journal. Verified in [research.md R4](./research.md): the task artifact was never written through the guarded boundary. The existing `IngestAgentGuardedWriteBoundaryRuleTests` whitelist is unchanged. |

**Result: PASS.** No entry in Complexity Tracking.

### Post-design re-check (after Phase 1)

Re-evaluated against `data-model.md`, `contracts/` and `quickstart.md`. **Still PASS**, with
three observations the design surfaced:

- **Principle I holds more strongly than expected.** The design adds no type, no namespace
  and no assembly — one options field, one resolver argument change per sub-path, one
  member on `ResolvedGrimoirePaths`. The four record stores keep their constructors' shape;
  only the resolved path they receive differs. Nothing in the design pushes toward a port.
- **Principle IV, one honest caveat.** This feature introduces no new observability signal
  at all — every row is a *widening* of an existing contract. That is the right call (a
  dedicated memory-root counter would answer no operator question), but it means the DoD's
  completeness audit must check the widened rows explicitly — a reviewer scanning for "new
  signals added by this feature" would find none and could wrongly conclude the section is
  vacuous. The contract in
  [contracts/paths-observability.md](./contracts/paths-observability.md) exists to make
  each widened field auditable by name.
- **Principle II's success-criteria split survived contact with the design.** The one place
  it was tempting to violate is SC-008: it would be easy to satisfy a lexical prompt check
  while silently degrading agent behavior. The original design kept SC-008 lexical and put
  the behavioral question where it belongs, on the existing evaluation thresholds after
  re-capture — no deterministic code was introduced to replace an agent judgment. **Revised
  2026-08-12 (T066)**: the lexical half of that design was itself later found to violate
  Constitution v1.11.0 Principle V, which reserves instruction-file *content* correctness
  exclusively for the evaluation tier — the dedicated lexical test has been removed; see
  spec.md's Assumptions section and the Test Strategy table's SC-008 row.
- **The configuration regrouping is a Principle IV improvement, with one accepted gap.**
  Turning the anchoring graph into the JSON tree and enforcing it with rule M5 converts a
  comment into a checked invariant, which is what "conventions not enforced by CI do not
  exist" asks for. The resulting key rename still fails *silently* for an operator who
  keeps exporting a pre-regrouping environment variable — an earlier draft closed that gap
  with superseded-key detection (FR-014), but that requirement was withdrawn on 2026-08-11
  as unwarranted machinery for a pre-1.0 project with no installations carrying old key
  names. The silent fallback is accepted as a bounded, pre-1.0 breaking-change consequence
  rather than guarded against.
- **Scope and spec are now aligned.** The regrouping is covered by FR-013/SC-009 after the
  2026-08-11 spec amendment, so `/speckit-analyze` should find no uncovered plan scope. The
  spec's own "other three roots out of scope" assumption carries an explicit, bounded
  exception for the key rename rather than being silently contradicted.

## Architectural Constraints & ADRs

*GATE: Agent MUST read all ADRs in `docs/adr/` before completing this section.*

| ADR | Title | Constraint on this feature |
| --- | --- | --- |
| ADR-022 | Minimal Directory Configuration Surface | **The binding constraint.** Rule R1 caps `PathSwitchCatalog.All` at exactly three named entries with 1:1 `HubPathSettings` parity; a fourth switch fails the build. Rule R2 bans code-level root defaults as IL literals. Its root/sub-path table anchors all four bookkeeping sub-paths at `WikiDir`, and its consequences record that placement as deliberate. All three must be amended — done by ADR-024, which additionally regroups the `Grimoire:Paths` key layout and so renames every configuration key and environment variable (switch names unaffected). Everything else it decided (mandatory config file, per-option precedence, no code defaults, one composition point, agent-build distribution, single launch mode) is preserved verbatim; "one options record" is read as one options graph bound from one section at one composition point. |
| ADR-024 | Memory Directory — A Fourth Independent Root | **Drafted by this plan; must be Accepted before `/speckit-tasks`.** Establishes `MemoryDir` as the fourth root, re-anchors the four sub-paths, amends R1 three→four, adds namespace-scoped rule M2 and wikilink tripwire M3, retires the wiki-relative task link, and — per the 2026-08-11 author directive — regroups `Grimoire:Paths` by anchoring root with rules M4 (options-graph shape) and M5 (grouping-is-anchoring invariant). |
| ADR-009 | Explicit Runtime Path Configuration | One composition point (`GrimoirePathOptions` + `GrimoirePathResolver`), no ambient process-context reads outside `Grimoire.Hub.Runtime.Paths`, fail-fast validation naming logical location + configured value + resolved path, auto-creation of writable locations, one startup report of every resolved location. The new root must be added through this composition point and nowhere else. |
| ADR-014 | Query Conversation Records | Owns the Conversation Record's on-disk home (`ConversationsDir`) and its `grimoire-conversation/1` format. The home is re-anchored by ADR-024; the format, the append-only lifecycle, the fail-closed read and the conversation-id charset rule are frozen (FR-010). |
| ADR-018 | Remediation Action Authorization and Execution | Introduced `RemediationTasksDir` and the record it holds. Same treatment: root moves, record format and state machine frozen. |
| ADR-003 | Domain vs. Operational State Persistence | Domain state is plain markdown; operational state is the embedded SQLite file. This feature does not reclassify anything — it restores ADR-003's *placement* intent (bookkeeping outside the knowledge base) that ADR-022 had reversed. The SQLite store stays under `DataDir`. |
| ADR-020 | Hub CLI Command Surface | Every CLI command accepts the path switches via a shared `HubPathSettings` base, parity-tested 1:1 against `PathSwitchCatalog.All`, and the root help's "Server options" section is generated from the catalog. The parity requirement is unchanged; the catalog and the help section each gain one entry, so `HubHelpUsageTests` must be updated. |
| ADR-007 | Agent Instruction Surface | Fixes the instruction document set (`system-prompt.md`, `policy.json`, Ingest-only `default-user-prompt.md`) and its fail-closed, SHA-256-traceable loading. FR-012's edits are content changes inside that fixed surface — no document is added, removed or renamed. |
| ADR-012 | Eval Runner and Recorded Replay | Manifest fingerprints over the instruction surface are the staleness authority and gate instruction-file merges. FR-012's prompt edits therefore *require* a full re-capture — see research R5. Recording location, format and fingerprint semantics are unchanged. |
| ADR-021 | Test Tier Taxonomy and Deterministic Waits | New tests land in the correct tier: architecture rules in `Grimoire.ArchTests` (Fast), path-resolution behavior in `Grimoire.IntegrationTests` (Integration). No fixed waits — `TestSupport.PollAsync` only, enforced by `DeterministicTierNoFixedWaitRuleTests`. |
| ADR-002 | Ingest Agent Execution Model | Each process owns its own artifact I/O, and the Hub holds no assembly reference to an agent. The `--tasks-dir` spawn argument keeps its name and meaning; only the value it carries changes. |
| ADR-006 | Agent Tool Loop and Guarded Boundary | Deny-by-default path-prefix policy anchored at the wiki root. No policy rule changes: the four folders leave the policy anchor entirely rather than gaining exclusions (research R4). |
| ADR-010 | Hexagonal Ports and Adapter Namespaces | Persistence and local-filesystem stores are port-exempt but containment-bound. The four record stores stay concrete classes in their existing namespaces. |
| ADR-017 | Log and Catalog Entry Format Enforcement | Constrains the `log.md` heading shape and the `index.md` catalog line shape only. Confirmed non-binding on the task-reference change (research R3): no regex requires a wikilink. |

**New ADR required?**: **Yes — drafted.** `docs/adr/ADR-024-memory-directory-root.md`
(status `proposed`). Per Constitution Principle III it MUST reach **Accepted** before
`/speckit-tasks` is invoked.

## Agentic Boundary (Constitution Principle V)

| Capability | Side | Where it lives |
| --- | --- | --- |
| Which folders the agent treats as reachable while browsing the wiki | Agentic core | `backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/system-prompt.md` — the reserved-harness-folder guidance and the Ingest directory-tree diagram entries are **removed** (FR-012) |
| How the agent references its run in the `log.md` paragraph | Agentic core | `backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md` — `Task: [[tasks/<task_id>.md]]` becomes `Task: <task_id>` |
| Resolving the memory root and its four sub-paths | Harness | `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs` |
| Declaring the configuration surface (options field, switch, config key) | Harness | `GrimoirePathOptions.cs`, `PathSwitchCatalog.cs`, `HubPathSettings.cs`, `appsettings.json` |
| Failing startup on a missing configuration key; auto-creating the root | Harness | `GrimoirePathResolver.cs` (`GrimoirePathConfigurationMissingException`, `CreateDirectoryIfMissing`) |
| Reporting resolved locations at startup | Harness | `GrimoirePathLogEvents.cs` |
| Writing task / conversation / findings / remediation records | Harness | `HubTaskArtifactWriter`, `ConversationRecordStore`, `FindingsReportStore`, `RemediationTaskRecordStore` — unchanged except for the path handed to them |
| The crash-recovery `log.md` backstop paragraph | Harness | `backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs` — drops the wikilink for the bare task id |

No wiki-content judgment moves into backend code. The two agentic rows are deletions and a
reference-format change inside instruction files, which is what Principle V's boundary
smell test requires: a change to what the agent considers part of the wiki is an
instruction-file change.

## Test Strategy

*MANDATORY: Every success criterion in spec.md MUST map to its primary verification method before tasks are generated.*

All doubles in play are pre-existing hand-rolled port fakes (`FakeModelClient`,
`FakeAgentProcess`). No mocking framework is referenced by any test project and none is
added. Every path assertion is state-based, against the real filesystem in a per-test temp
directory built by `PathConfigurationTestHelpers.SeedRequiredInputs`.

| Success criterion | Category | Primary test type | Doubles / external dependencies | Fixtures / sampled data | Notes |
| --- | --- | --- | --- | --- | --- |
| **SC-001** — 100% of tasks/conversations/findings/remediation records resolve under the memory folder | Deterministic guarantee | Hermetic integration test (`Grimoire.IntegrationTests/PathConfiguration`) | Real filesystem in temp dir; real configuration binder; no LLM | Seeded agent-runtime inputs via `PathConfigurationTestHelpers` | Extends the existing `WikiDirIsolationTests` "all four live under WikiDir" assertion into its inverse: all four under `MemoryDir`, none under `WikiDir`. End-to-end coverage comes from the existing `FindingsPathTests`, `RemediationTasksPathTests`, `QueryRuntimePathsTests`, `LintWikiDirEndToEndContentTests`, all re-pointed. |
| **SC-002** — 100% of relocations of any root leave the other three unchanged | Deterministic guarantee | Hermetic integration test | Real filesystem, real binder | Four relocation matrices (relocate each root alone) | Delivered by `PathGroupingInvariantTests` (rule M5), which generalizes the 3×3 `SiblingDirectoryLayoutTests` matrix to a reflection-driven per-group assertion. Covers US2 AS1–AS3 directly. |
| **SC-003** — 100% of memory-folder resolutions honor CLI > env > config file | Deterministic guarantee | Hermetic integration test | Real filesystem, real binder, real `AddCommandLine` mapping | Three-tier precedence fixture per `PathPrecedenceTests` | Also asserts `PathLocation.Source` reports `command-line` / `environment` / `config-file` correctly for `memory_dir`. Must exercise the **nested** env-var form `Grimoire__Paths__Memory__Dir` — the flat form silently no-ops, which is the failure mode this test is the only guard against. |
| **SC-004** — 100% of starts against a config file missing the key fail naming that key | Deterministic guarantee | Hermetic integration test | Real filesystem, real binder | Config file with the `Memory` group's `Dir` omitted, and separately with the whole `Memory` group omitted | Asserts `GrimoirePathConfigurationMissingException.MissingKeys` contains the full key path `Grimoire:Paths:Memory:Dir` and the message names `appsettings.json`. Both the missing-key and missing-group cases must be covered — the group-property initializer makes them different code paths. Extends `StartupValidationTests`. Doubles as the behavioral backstop for rule M2 (research R2). |
| **SC-005** — 100% of starts create the memory folder when absent | Deterministic guarantee | Hermetic integration test | Real filesystem | Temp dir with no `memory/` present | Asserts the directory exists after `Resolve` and that `paths_location_created` was emitted for `memory_dir`. Extends `ZeroConfigStartupTests` / `DefaultLayoutTests`. |
| **SC-006** — memory folder appears in 100% of startup path-resolution reports | Deterministic guarantee | Hermetic observability contract test | Real telemetry registration from the production composition root (Principle IV: no test-only provider) | In-memory log/activity capture attached to the real pipeline | Extends `PathLoggingContractTests` (mandatory field `memory_dir`, level Information) and `PathTracingContractTests` (span tag `memory_dir`). |
| **SC-007** — 0% of pre-existing on-disk records moved automatically | Deterministic guarantee | Hermetic integration test | Real filesystem | Temp dir pre-seeded with records under `<WikiDir>/tasks`, `<WikiDir>/conversations`, … | Asserts, after a full resolve + start, that every pre-seeded file is still byte-identical at its original path and that the memory root contains none of them. This is a negative guarantee, so it needs its own test rather than being implied. |
| **SC-008** — 0% of the three instruction files reference these folders as wiki-reachable | Deterministic guarantee, narrowed (see below) | ~~Hermetic content assertion over the instruction sources (Fast tier)~~ **Revised 2026-08-12 (T066)**: no dedicated test — Constitution v1.11.0 Principle V bans a deterministic test that string-matches instruction-file content. Corroborated only by ADR-024 rule M3 (wikilink form only) and the FR-002/FR-012 evaluation row below. | None — reads the real `Instructions/system-prompt.md` files from `backend/src/` (at authoring time only; no longer at test time) | The three real prompt files | The original design added `InstructionFilesWikiScopeTests`, a lexical assertion that none of `tasks/`, `conversations/`, `findings/`, `remediation-tasks/`, `[[tasks/` appears in any of the three prompts. That test has been removed (T066): it is exactly the pattern Principle V's carve-out — added in the same constitution amendment this PR's own review triggered — now prohibits. See spec.md's Assumptions section for the accepted, narrower guarantee. |
| **SC-009** — 100% of sub-paths resolve against the root they are grouped with, 0% against another | Deterministic guarantee | Hermetic integration test, reflection-driven | Real filesystem, real binder | Per-group relocation over the options graph | `PathGroupingInvariantTests` (ADR-024 rule M5). Reflection over `GrimoirePathOptions` means a sub-path added later is covered without editing the test — the point is to keep the grouping true, not to snapshot today's key list. Also delivers SC-002. |
| **FR-002 / FR-012 behavioral regression** — removing the reserved-folder guidance does not degrade agent behavior | Agent-judgment threshold | Evaluation with threshold (existing SlowEval tier, re-captured) | Recorded LLM responses via `ReplayModelClient`; capture needs a live provider | All 22 scenarios, 230 samples, re-captured | **Not a new criterion** — the spec correctly declares no new agent-judgment outcome. Listed because the existing scenario thresholds are the only evidence that FR-012's deletions were harmless, and because re-capture is a gating step (research R5). Ingest `convention-adherence` and `log-paragraph-specificity` are the two scenarios most exposed to the task-reference change. |

**Structural tests (Phase 0, before feature code)** — each with a Red/Green probe per
Constitution Principle III:

| Rule | Test | Probe |
| --- | --- | --- |
| ADR-024 M1 | `Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests` (updated: three → four named entries, `HubPathSettings` declares exactly 4) | Add a fifth catalog entry; verify red; remove |
| ADR-024 M2 | `Grimoire.ArchTests/NoCodeLevelPathDefaultsRuleTests` (new namespace-scoped case for `memory` in `Grimoire.Hub.Runtime.Paths`) | Add `const string DefaultMemoryDirName = "memory"` to `GrimoirePathOptions`; verify red; remove |
| ADR-024 M3 | `Grimoire.ArchTests/NoWikiRelativeHarnessRecordLinkRuleTests` (new; IL literal scan for `[[tasks/`, `[[conversations/`, `[[findings/`, `[[remediation-tasks/`) | Restore `RestartReconciler`'s `Task: [[tasks/{taskId}.md]]`; verify red; remove |
| ADR-024 M4 | `Grimoire.ArchTests/PathOptionsGroupingRuleTests` (new; reflection over `GrimoirePathOptions` — exactly four group properties each declaring `Dir`, plus `SecretsFile`, and no loose path property) | Add a loose `TasksDir` string property directly to `GrimoirePathOptions`; verify red; remove |
| ADR-024 M5 | `Grimoire.IntegrationTests/PathConfiguration/PathGroupingInvariantTests` (new; **Integration tier** — needs the real resolver and filesystem, so it is not a Phase 0 test) | Anchor a `Memory` group sub-path at `dataDir` in `GrimoirePathResolver`; verify red; revert |

M5 is the one rule that cannot be a Phase 0 structural test: it asserts resolver *behavior*,
which requires the resolver to run. It lands with the resolver work in the US1 phase, and
it **subsumes** the 4×4 root-independence matrix that SC-002 would otherwise need as a
standalone test — relocating each group's `Dir` and checking what moved is precisely the
independence assertion, generalized over the options graph by reflection so a future
sub-path is covered without editing the test.

**Sequencing constraint**: the FR-012 instruction edits and the eval re-capture belong in
the **final** implementation phase. Every prior phase then stays green against the existing
recordings, and the re-capture happens once against the finished prompt text rather than
repeatedly. `scripts/test-fast.sh` will not surface the staleness failure — only the full
`dotnet test backend/tests/Grimoire.AgentEvals` will.

**Constitution III classification and the Feature-Scoped-Invariant escape valve** (added
2026-08-12, convergence task T067 — Constitution v1.11.0 landed before this plan was first
authored and binds it in full, but this classification was originally omitted): per
[ADR-024](../../docs/adr/ADR-024-memory-directory-root.md)'s Structural Enforcement
section (the classification was briefly recorded in an amending ADR-026, then merged back
into ADR-024 itself before this branch merged to `main` — see that document's history
note),
M1, M2, and M4 above are each a **Feature-Scoped Invariant** — they match Principle III's
own worked examples ("the CLI exposes exactly N named path switches," "no code-level
literal duplicates a config default," "the options graph mirrors the config file's
grouping") verbatim. Principle III's default for a Feature-Scoped Invariant is a
classicist, state-based behavioral test, not the Phase 0 reflection/IL form — *unless*
this plan explicitly justifies why no runtime-observable behavior can catch the violation
before merge. That justification, per rule:

- **M1** (exact four-switch CLI surface): a behavioral equivalent exists —
  `HubHelpUsageTests` (T047) asserts `--help`'s "Server options" section lists exactly the
  four switches with 1:1 `PathSwitchCatalog.All` parity, out-of-process, against the real
  CLI. That test alone would catch a fifth switch added *and* wired into the help output.
  It would **not** catch a fifth `[CommandOption]` added to `HubPathSettings` without a
  matching catalog entry, or a sixth path-shaped field added elsewhere that never reaches
  `--help` at all — `DirectorySwitchSurfaceRuleTests`' IL scan is exhaustive over every
  production assembly, not just the one path a manual test author thought to enumerate.
  Kept as a reflection test for that exhaustiveness; `HubHelpUsageTests` stays as the
  behavioral corroboration, not a replacement.
- **M2** (no code-level `memory` literal in `Grimoire.Hub.Runtime.Paths`): `SC-004`'s
  `StartupValidationTests`/`PathLoggingContractTests` are the behavioral backstop for the
  *scenario* this rule guards against (research R2) — a missing `Memory:Dir` key fails
  loudly instead of silently defaulting to `memory`. They do not catch a hardcoded
  `?? "memory"` fallback added on some other, untested code path within the namespace;
  only an exhaustive IL scan does. Kept as a reflection test for the same reason as M1.
- **M4** (options-graph shape — four groups plus one ungrouped `SecretsFile`): no
  behavioral test can distinguish "the graph has this shape" from "the graph has some
  other shape that happens to resolve the same four roots correctly today" — a stray
  ungrouped path property added alongside the groups would not necessarily fail M5's
  relocation-behavior assertions (M5 only exercises the groups that already exist; an
  unrelated loose property is simply never touched by it, so it can regress silently). No
  runtime-observable behavior catches that regression before merge. Kept as a reflection
  test; this is the one rule in this list with no partial behavioral corroboration at all.

No test, statement, or probe changes as a result of this classification — it records why
the existing Phase 0 tests for M1/M2/M4 remain, per Principle III's escape valve, rather
than being replaced or supplemented.

## Observability

*MANDATORY: Code without this instrumentation fails the Definition of Done.*

This feature introduces **no new signal**. Everything is a *widening*: the mandatory-field
set of one existing log event and one existing span, and the trigger surface of two more
existing signals. Every widened row below is treated as a contract change and carries
implementation, deterministic-test and CI obligations exactly as a new row would.

### Business Metrics (OpenTelemetry Counters / Gauges)

| Metric name | Type | Description | Labels |
| --- | --- | --- | --- |
| `grimoire.hub.path_resolution_failures_total` | Counter (existing, `HubMetrics.RecordPathResolutionFailure`) | Incremented once when path resolution aborts. **Widened trigger**: `reason=configuration_missing` now also fires when `Grimoire:Paths:Memory:Dir` is absent from every configuration tier (or its whole group is). | `reason` ∈ `configuration_missing`, `agent_directory_empty`, `location_invalid` |

No new metric is warranted: the memory root is one more location in an existing resolution
step, and inventing `grimoire.hub.memory_dir_*` would create a counter with no operator
question behind it.

### Structured Log Events

| Event | Level | Trigger | Mandatory fields |
| --- | --- | --- | --- |
| `paths_resolved` (existing, EventId 40) | INFO | Once per successful path resolution at process start | `data_dir`, `wiki_dir`, `agent_dir`, **`memory_dir`** *(new mandatory field — FR-008/SC-006)*, `secrets_file`, `state_db`, `raw_dir`, `sources` (the `sources` list gains a `memory_dir=<source>` pair) |
| `paths_location_created` (existing, EventId 41) | INFO | Each writable location auto-created because it was absent. **Widened trigger**: now fires for `memory_dir` and for the four bookkeeping sub-paths at their new anchor | `location`, `resolved_path` (unchanged) |
| `paths_configuration_missing` (existing, EventId 43) | ERROR | Configuration binding produced an empty value for one or more roots. **Widened trigger**: the memory root joins the checked set, and an entirely absent group reaches this gate too | `configuration_file`, `missing_keys` — **values become full key paths** (`Grimoire:Paths:Memory:Dir`) instead of bare field names |

**Derivation rule (MANDATORY)**: Every row above MUST map to concrete work in `tasks.md`
covering all three categories:

1. **Implementation** — `GrimoirePathLogEvents.LogPathsResolved` gains the `memory_dir`
   tag and message placeholder; `GrimoirePathResolver` adds `MemoryDir` to the
   missing-root-key check and to the auto-created location set.
2. **Deterministic integration tests** —
   `Grimoire.IntegrationTests/PathConfiguration/PathLoggingContractTests` asserts event
   name, `LogLevel.Information`, and every mandatory field including `memory_dir`;
   a `paths_location_created` case asserts `location=memory_dir` on a cold start; a
   `paths_configuration_missing` case asserts level Error and `missing_keys` containing
   `MemoryDir`.
3. **CI enforcement** — these tests live in `Grimoire.IntegrationTests`, which
   `.github/workflows/ci.yml` already runs on every PR. The task is to confirm the new
   cases execute there (no workflow edit expected); if the confirmation fails, the fix is
   a workflow task, not a waiver.

### Distributed Trace Spans (OpenTelemetry)

| Span name | Parent span | Attributes |
| --- | --- | --- |
| `paths_resolved` (existing, started by `GrimoirePathLogEvents.StartLogEventSpan`) | root — emitted during host composition, before any request activity exists | `signal_type=log`, `event_name=paths_resolved`, `level=Information`, `data_dir`, `wiki_dir`, `agent_dir`, **`memory_dir`** *(new required attribute)*, `secrets_file`, `state_db`, `raw_dir`, `sources` |

**Derivation rule (MANDATORY)**: Every row above MUST map to concrete work in `tasks.md`
covering all three categories:

1. **Implementation** — `StartLogEventSpan("paths_resolved", …)` gains
   `span?.SetTag("memory_dir", paths.MemoryDir)`, set inside the same span scope as the
   log call so log and span stay correlated.
2. **Deterministic integration tests** —
   `Grimoire.IntegrationTests/PathConfiguration/PathTracingContractTests` asserts the span
   name, that it is a root span in this composition (no unsampled parent — the Principle IV
   failure mode that ADR-005's rationale calls out), and the presence and value of the
   `memory_dir` attribute.
3. **CI enforcement** — same `Grimoire.IntegrationTests` PR gate as above; confirm the new
   assertions run there.

**Contract tests exercise production wiring** (Principle IV): the existing
`PathConfiguration` observability tests obtain signals from the real telemetry registration
used by `HubHostComposition`, not from a hand-registered `ActivitySource` or an always-on
test sampler. The new assertions extend those tests in place and MUST NOT introduce a
test-only provider.

## Project Structure

### Documentation (this feature)

```text
specs/022-memory-directory-root/
├── plan.md              # This file (/speckit-plan command output)
├── spec.md              # Feature specification
├── research.md          # Phase 0 output — R1..R7 decisions
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── directory-options.md      # The four-root configuration surface
│   └── paths-observability.md    # paths_resolved log + span contract
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)

docs/adr/
└── ADR-024-memory-directory-root.md   # Drafted by this plan; must be Accepted
```

### Source Code (repository root)

```text
backend/src/Grimoire.Hub/
├── appsettings.json                          # regrouped into Data/Wiki/Agent/Memory
│                                             #   groups + ungrouped SecretsFile (R8)
├── Runtime/Paths/
│   ├── GrimoirePathOptions.cs                # flat fields → four group types
│   │                                         #   (DataPathOptions, WikiPathOptions,
│   │                                         #   AgentPathOptions, MemoryPathOptions);
│   │                                         #   groups initialized, leaf values not
│   ├── GrimoirePathResolver.cs               # + memory root resolution, missing-key check
│   │                                         #   (full key paths), location descriptor,
│   │                                         #   auto-create; four sub-paths re-anchored
│   │                                         #   to memoryDir; options.X → options.G.X
│   ├── ResolvedGrimoirePaths.cs              # + MemoryDir member — stays flat (research R6)
│   ├── PathSwitchCatalog.cs                  # + --memory-dir; all four ConfigKeys nested;
│   │                                         #   --wiki-dir description narrowed
│   └── GrimoirePathLogEvents.cs              # + memory_dir log field and span tag
├── Cli/HubPathSettings.cs                    # + [CommandOption("--memory-dir <PATH>")]
├── ContentRoot/IngestContentPaths.cs         # TasksDir now projected from MemoryDir, not Root
└── OperationalState/RestartReconciler.cs     # [[tasks/{id}.md]] → bare task id (rule M3)

backend/src/Grimoire.EvalRunner/
└── Workspace/EvalWorkspace.cs                # TasksDir becomes a WikiRoot sibling;
                                              # PageFiles() tasks filter deleted (research R7)

backend/src/Grimoire.IngestAgent/Instructions/system-prompt.md   # FR-012: remove skip list,
backend/src/Grimoire.QueryAgent/Instructions/system-prompt.md    #   remove 4 tree lines +
backend/src/Grimoire.LintAgent/Instructions/system-prompt.md     #   trailing paragraph,
                                                                 #   de-wikilink task ref

backend/tests/Grimoire.ArchTests/
├── DirectorySwitchSurfaceRuleTests.cs             # M1: three → four (updated)
├── NoCodeLevelPathDefaultsRuleTests.cs            # M2: + namespace-scoped `memory` case
├── NoWikiRelativeHarnessRecordLinkRuleTests.cs    # M3: new IL tripwire
└── PathOptionsGroupingRuleTests.cs                # M4: new options-graph shape rule

backend/tests/Grimoire.IntegrationTests/
├── PathConfiguration/                        # WikiDirIsolationTests (inverted),
│   │                                         # DefaultLayoutTests, PathPrecedenceTests,
│   │                                         # StartupValidationTests, FindingsPathTests,
│   │                                         # RemediationTasksPathTests, QueryRuntimePathsTests,
│   │                                         # CustomAgentDirEndToEndTests,
│   │                                         # LintWikiDirEndToEndContentTests,
│   │                                         # PathLoggingContractTests, PathTracingContractTests,
│   │                                         # PathConfigurationTestHelpers, + new SC-007 test
│   └── PathGroupingInvariantTests.cs         # M5: new; also delivers SC-002
├── Fakes/TestResolvedGrimoirePathsFactory.cs # four sub-paths under a memory root
├── Fakes/IngestSubmissionPipelineFixture.cs  # same
├── SiblingDirectoryLayoutTests.cs            # superseded by PathGroupingInvariantTests
├── HubHelpUsageTests.cs                      # help output ⊇ four-entry catalog (ADR-020)
└── (≈30 further files that construct these paths, mostly via the two fakes above)

backend/tests/Grimoire.AgentEvals/
└── EvalIndependenceFromHubConfigurationTests.cs   # 11-entry env-var array → nested names

backend/tests/Grimoire.AgentEvals/Fixtures/recordings/   # all 22 scenarios re-captured

.gitignore                                    # + memory/ ; llm-wiki/ comment corrected
docs/operations/runtime-configuration.md      # "three roots" → four throughout
```

**Structure Decision**: No new project, assembly, namespace or layer. The feature extends
the existing single composition point in `Grimoire.Hub.Runtime.Paths` (ADR-009) with one
root and re-points four anchor arguments; every other production edit is a consumer
following that move. The four new option group types (`DataPathOptions`, `WikiPathOptions`,
`AgentPathOptions`, `MemoryPathOptions`) are nested inside that same namespace and file
neighbourhood — ADR-009's "one options record" becomes one options *graph* bound from one
section at one composition point, which preserves the rule's substance (a single place to
look) rather than its incidental flatness. Architecture tests live in the existing
`Grimoire.ArchTests`, path behavior in the existing
`Grimoire.IntegrationTests/PathConfiguration` folder — the same homes ADR-022's own
implementation used, which keeps the four roots' tests side by side rather than splitting
the third from the fourth.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution Check violations. Table intentionally empty.

The one deliberate narrowing — ADR-024 rule M2 scanning a single namespace where ADR-022
rule R2 scans every production assembly — is not a constitutional violation (the rule
exists, is structural, has a Red/Green probe and a CI gate) but is recorded as an accepted
trade-off in ADR-024's Consequences and in [research.md R2](./research.md), together with
the behavioral backstop that covers the gap.
