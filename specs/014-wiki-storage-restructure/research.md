# Research: Wiki Storage Layout & Shared Log/Catalog Format

Phase 0 output for `specs/014-wiki-storage-restructure/spec.md`. Decisions below are
grounded in the current implementation, verified by direct inspection:
`backend/src/Grimoire.Hub/Runtime/Paths/{GrimoirePathOptions,GrimoirePathResolver,ResolvedGrimoirePaths}.cs`,
`backend/src/Grimoire.Domain/Guardrails/{SafetyPolicy,PolicyDecision}.cs`,
`backend/src/Grimoire.AgentRuntime/Instructions/PolicyLoader.cs`,
`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`,
`backend/src/Grimoire.IngestAgent/IngestLog/IngestLogAppender.cs`,
`backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs`,
`data/agents/{ingest,query,lint}/{policy.json,system-prompt.md}`,
`backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs`,
`docs/adr/ADR-{002,003,006,007,009}-*.md`, `docs/llm-wiki-magrathea-{claude,skill}.md`.

## R1: Remove the `pages/` wrapper by collapsing `PagesDir` into `ContentRoot`

- **Decision**: Delete the `PagesDir` concept entirely rather than keep it as an alias
  equal to `ContentRoot`. `GrimoirePathResolver.cs:67`'s
  `Path.Combine(contentRoot, "pages")` and `ResolvedGrimoirePaths.PagesDir` are
  removed; every consumer (`IngestRunCoordinator.cs:206`, `QueryRunCoordinator.cs:156`,
  `SubmissionService.cs:52`, `AgentProcessHost.cs:273,390`, `IngestCliOptions.cs`,
  `QueryCliOptions.cs`, `LintCliOptions.cs`, `ContentRootPaths.cs`,
  `EvalWorkspace.cs:20`, and ~15 test files) is repointed at `ContentRoot` directly.
  The internal CLI flag the Hub passes to spawned agent processes,
  `--pages-dir`, is renamed to `--content-root` end-to-end (it is a private
  Hub↔agent-process contract, not a public API — ADR-002).
- **Rationale**: FR-001/FR-002 require articles directly under
  `<content-root>/<category>/...`, with no wrapper segment. Keeping a `PagesDir`
  property that happens to equal `ContentRoot` would satisfy the letter of the FR but
  leave a permanently misleading name (there is no "pages" folder to point at). Since
  every call site is a mechanical rename (change the referenced property/flag, not the
  logic), doing the rename now is lower long-term cost than carrying a vestigial alias.
- **Alternatives considered**: keep `PagesDir` as an alias for `ContentRoot`
  (rejected — cheaper diff today, but permanently confusing; the option name would
  forever contradict the folder layout it configures); make `PagesDir` configurable
  independently again in the future if a wrapper is ever reintroduced (rejected — YAGNI,
  no such requirement exists, and Assumptions explicitly puts category-folder naming in
  agents' hands, not a fixed subfolder).

## R2: `TasksDir` and `ConversationsDir` become base-anchored siblings of `ContentRoot`

- **Decision**: Add a `TasksDir` field to `GrimoirePathOptions` (mirroring the existing
  `ConversationsDir`/`WriteLocksDir`/`FindingsDir` pattern: optional override +
  `DefaultTasksDirName = "tasks"` const), resolved against `baseDir` — not
  `contentRoot`. Change `ConversationsDir`'s resolution anchor from `dataDir`
  (`GrimoirePathResolver.cs:59`) to `baseDir` as well; its option field, default name,
  and doc comment (`GrimoirePathOptions.cs:49-54`) stay, only the anchor argument
  changes. Both get a `BuildLocation` entry (source-tracking, matching every other
  independently-configurable location) and their own `CreateDirectoryIfMissing` call —
  today `tasksDir` is created (`GrimoirePathResolver.cs:121`) but was never in the
  `locations` list at all, since it had no independent option field before.
- **Rationale**: FR-003 (tasks) and FR-004 (conversations) both require a sibling of
  the content root, not nested inside it (tasks, today) and not nested inside the
  internal data directory (conversations, today). `baseDir` is the one anchor both
  `ContentRoot` and `DataDir` already resolve against
  (`GrimoirePathResolver.cs:49-51`), so anchoring `TasksDir`/`ConversationsDir` there
  too makes them true siblings without introducing a new anchor concept.
  `DataDir` itself, and everything under it (`RawDir`, `StateDb`, `SecretsFile`,
  `InstructionsDir`/`QueryInstructionsDir`/`LintInstructionsDir`, `WriteLocksDir`,
  `FindingsDir`), is untouched — FR-005.
- **Alternatives considered**: introduce a distinct "workspace root" anchor separate
  from `baseDir` (rejected — `baseDir` already is that anchor; a second one would
  duplicate ADR-009's single composition point for no benefit).

## R3: Guardrail policy prefixes — root-directory catch-all, ordered after exact-match `index.md`/`log.md`

- **Decision**: In each `data/agents/*/policy.json`, replace `{"pathPrefix": "pages/"}`
  with a single prefix that matches the whole content root as a directory (implementation:
  extend `PolicyLoader.NormalizeRulePrefix` — currently `IsNullOrWhiteSpace` drops an
  empty prefix as inert, `PolicyLoader.cs:163-166` — with one new explicit case: the
  literal sentinel `"."` normalizes to the policy's `_wikiRoot` anchor itself, treated
  as directory-style (matches the anchor and everything under it), the same way a
  trailing-slash prefix like `"pages/"` does today
  (`SafetyPolicy.PrefixMatches`, `SafetyPolicy.cs:169-181`). This new `"."` rule is
  placed **after** the existing exact-match `"index.md"`/`"log.md"` entries in both the
  `read` and `write` arrays. Evaluation is first-match-wins, deny-by-default, with no
  deny/exclude rule type (`SafetyPolicy.Evaluate`, `SafetyPolicy.cs:128-159`) — so
  ordering, not rule specificity, is what keeps `index.md`/`log.md` on their own
  existing (already-correct) rule instead of falling through to the new catch-all's
  mode (e.g. Query's `create-only` article rule must never apply to `index.md`, which
  Query already updates on every turn).
- **Rationale**: Category subfolder names are open-ended, chosen by agents
  (spec Assumptions) — policy cannot enumerate them. The exact-match test suite
  (`SafetyPolicyTests.cs:79-106`) already proves exact-match prefixes only match the
  literal top-level file, never a same-named file nested in a category folder (e.g.
  `concepts/index.md` is a normal article, not the catalog) — so the ordering is safe,
  not just convenient.
- **Alternatives considered**: a `pathPrefix` per known category (rejected — categories
  are agent-chosen and open-ended, spec Assumptions); a real deny-rule type added to the
  policy schema so ordering wouldn't matter (rejected — larger schema change than this
  feature needs, no other current or anticipated rule needs negation); resolving `"."`
  implicitly via `IsNullOrWhiteSpace` instead of a distinct literal (rejected — that
  silently changes today's "empty prefix is inert" behavior for every already-deployed
  policy file that might have a stray empty string, whereas a new explicit non-empty
  sentinel is additive and unambiguous). A `PolicyLoader` unit test pins this new
  normalization case and the ordering-dependent index.md/log.md behavior
  (Red/Green-style regression, since `SafetyPolicyTests.cs` already documents the
  exact-match/prefix-match distinction this decision depends on).

## R4: Ingest's `tasks/` guarded-tool policy entry is dropped, not re-anchored

- **Decision**: Remove `{"pathPrefix": "tasks/"}` from `data/agents/ingest/policy.json`
  (currently the only agent with it) rather than pointing it at the new sibling
  `TasksDir`.
- **Rationale**: `TaskArtifactStore.WriteAsync`/`ReadAsync`
  (`TaskArtifactStore.cs:9-19`) do plain direct file I/O — they are called by harness
  code around the agent loop, never through the guarded `write_file`/`read_file` tool
  the LLM invokes, and Task Artifact lifecycle is explicitly harness territory
  (Constitution Principle V). `PolicyLoader`'s `_wikiRoot` anchor
  (`PolicyLoader.cs:25,32`, constructed from `run.WikiRoot` in
  `AgentHost.cs:139`) is the content root the agent's own tool calls are scoped to;
  once tasks live outside that root as a true sibling (R2), there is no correct anchor
  left inside the single-root policy model to point a `tasks/` prefix at without
  widening the agent's guarded-tool scope beyond the wiki content root entirely — a
  bigger boundary change this feature does not require. The agent already references
  task artifacts as inert wikilink text (`[[tasks/{taskId}.md]]`) in log entries, which
  needs no file access.
- **Alternatives considered**: give `PolicyLoader` a second, independent anchor for
  `TasksDir` so agents keep guarded read/write access to it (rejected — no acceptance
  scenario or FR asks agents to read/write task files directly; this would be new
  surface area the spec doesn't require, and widens the guarded boundary rather than
  narrowing it with the layout change).

## R5: Unified `log.md` heading format replaces both today's conventions, via one shared `WikiLogAppender`

- **Decision**: The target heading is exactly `## [DATE] TYPE | SUMMARY` (level `##`,
  per spec Assumptions and `docs/llm-wiki-magrathea-claude.md:18`'s
  `## [DATUM] operation | Titel` reference shape), `DATE` = `yyyy-MM-dd`, followed by a
  blank line and a short prose paragraph — replacing **both** existing conventions:
  the agent-instruction bullet format (`data/agents/ingest/system-prompt.md:301`,
  `* **Verb**: <text>` under a `## YYYY-MM-DD` date-only heading) and the backstop's
  own different heading shape (`IngestLogAppender.cs:49,71`,
  `## [{date}] ingest | {outcome} | source: ... | task: [[...]]` — multiple
  pipe-delimited fields crammed into one line, no paragraph). The backstop mechanism
  itself is generalized: `Grimoire.IngestAgent.IngestLog.IngestLogAppender` moves to
  `Grimoire.AgentRuntime` (the library already shared by all three agent processes —
  `Composition/Core/Guardrails/Host/Instructions/RunEvents/Telemetry`, ADR-002/ADR-008)
  as `WikiLogAppender`, parameterized by `TYPE` (`ingest`/`query`/`lint`) instead of
  hardcoded to `"ingest"`, and wired into `Grimoire.QueryAgent`/`Grimoire.LintAgent`,
  neither of which has a backstop today.
- **Rationale**: FR-007/FR-008/FR-009 require one identical heading-plus-paragraph
  shape across every agent type with "no agent-type-specific variation in structure";
  FR-010 requires the fallback in the *same* format for every agent type, and today
  only Ingest has a fallback at all. Detail that used to live in the heading
  (`source:`, `task:`, `outcome`) moves into the paragraph body — the heading carries
  only `DATE`, `TYPE`, `SUMMARY` per spec's literal grammar.
- **Alternatives considered**: keep per-agent-type backstop classes with the shared
  format only documented, not code-shared (rejected — three copies of the same format
  string is exactly the drift this feature exists to end); keep the bulleted
  `* **Verb**:` sub-structure inside the paragraph (rejected — spec calls for "a short
  prose paragraph," not a re-encoded bullet).
- **Structural enforcement**: SC-003/SC-004 state heading conformance and locatability
  as 100% guarantees — instruction-file convention (what `WikiLogAppender` and
  `system-prompt.md` both aim for) cannot itself make that claim true for
  agent-authored writes. `docs/adr/ADR-017-log-and-catalog-entry-format-enforcement.md`
  adds the enforcing mechanism: a guarded-write-boundary check that denies any
  non-append or malformed-heading write to `log.md`, composed with the existing
  `WriteMode`/CAS checks. See ADR-017 for the full mechanism; the same ADR covers
  R6's `index.md` shape check.

## R6: `index.md` catalog format — instruction-file convention, plus structural shape enforcement (ADR-017)

- **Decision**: The catalog line format (`- [link](path) — <description> — <status>`,
  link + short description + trailing source-status marker, written in the wiki's
  configured content language) is documented in
  `data/agents/{ingest,query}/system-prompt.md`'s "Catalog (index.md) Upkeep" section
  (`system-prompt.md:276-291`) — the *description*/*status* content itself stays
  agent-judgment territory (SC-007's ≥90% threshold), per Constitution Principle V.
  But SC-006 states catalog-entry *shape* conformance as a 100% guarantee, the same
  class of claim `log.md`'s SC-003/SC-004 make — so it gets the same treatment as R5's
  log heading check, not none at all: `docs/adr/ADR-017-log-and-catalog-entry-format-enforcement.md`
  adds a structural check at the guarded write boundary that denies any brand-new
  `- [`-led line in a proposed `index.md` write that doesn't match the link—description—status
  shape, without touching existing lines or surrounding structure (section headings,
  `(superseded)` markers on unchanged lines).
- **Rationale**: Constitution Principle II — a spec-stated 100% guarantee needs an
  enforceable mechanism, not just instruction-file convention (which cannot itself
  guarantee compliant output, since agent behavior isn't guaranteed-conforming by
  construction). The check stays mechanical (shape only, never judging whether a
  description is *good*), so it doesn't cross into reimplementing agent judgment —
  Principle V's boundary holds. No backend "index appender"/backstop component is
  introduced, unlike `log.md`'s `WikiLogAppender` (R5) — the spec has no
  fallback/backstop requirement for `index.md` (FR-010 names only `log.md`), so there
  is nothing to generalize here beyond the new validator.
- **Alternatives considered**: leave catalog format as convention-only, relying on
  SC-007's evaluation threshold as the only check (rejected on the same Principle II
  grounds ADR-017's Context section states for `log.md` — this was this research's
  first-pass answer and was revised once SC-006's literal "100%" wording was checked
  against the constitution's success-criteria split rule); a deterministic backend
  *formatter* that composes catalog lines from structured fields (rejected — would put
  the harness in the business of writing wiki content, not validating its shape, a
  larger Principle V risk than a post-hoc regex check). Lint's `system-prompt.md` gets
  no catalog-format section — Lint never writes `index.md`
  (`data/agents/lint/policy.json` has no write rule for it).

## R7: No migration path — confirmed, not a design decision

FR-006 and the spec's Clarifications settle this directly: Grimoire has no production
deployment, the content root starts empty, so there is no prior-layout content to
migrate. This feature changes only the default configuration values and internal
directory structure; no data-migration code, script, or compatibility shim is written
or planned.

## R8: Test and fixture fallout

Fixtures/tests asserting today's `pages/` wrapper or `wiki/tasks`/`data/conversations`
locations are updated in place (not duplicated): `PathConfiguration/*.cs` (5 files),
`IngestTaskRecordWatcherTests.cs`, `Grimoire.EvalRunner/Workspace/EvalWorkspace.cs`
and its `Scoring/{DeterministicScorers,LintDeterministicScorers}.cs` consumers,
`backend/tests/Grimoire.AgentEvals/Fixtures/lint-seeded-defects/wiki/`. No test today
asserts on `log.md`/`index.md` heading format — R5/R6 are new deterministic (heading
shape, FR-007/FR-008/FR-009/FR-011) and evaluation-threshold (paragraph/description
quality, SC-005/SC-007) coverage, not a fixture update.
