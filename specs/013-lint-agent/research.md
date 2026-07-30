# Research: Lint Agent — Wiki Health Check

Phase 0 output for `specs/013-lint-agent/spec.md`. Decisions below resolve every open
point the spec's Assumptions hand to planning, above all the superseding write-scope
mechanism recorded as `docs/adr/ADR-016-lint-write-scope-frontmatter-only-enforcement.md`
(status `proposed`, extends ADR-015).

Code facts referenced here were verified against the current implementation (post
feature 012 merge): `backend/src/Grimoire.AgentRuntime/Host/AgentProfile.cs`,
`backend/src/Grimoire.Domain/Guardrails/{SafetyPolicy,PolicyDecision}.cs`,
`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`,
`backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs`,
`backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs`,
`backend/src/Grimoire.Hub/QueryDispatch/QueryRunCoordinator.cs`,
`backend/src/Grimoire.Hub/QueryConversations/ConversationRecordFormat.cs`,
`backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs`,
`data/agents/ingest/system-prompt.md`, `docs/llm-wiki-magrathea-skill.md`.

## R1: Why a new write-scope mode is needed, and why it extends rather than amends ADR-015

- **Decision**: Add a third `WriteRule`/`PolicyDecision` mode, `frontmatter-only`,
  drafted as a new ADR-016 that extends ADR-015 rather than amending its already-
  `accepted` text.
- **Rationale**: FR-010/SC-002 require Lint's "frontmatter only, never body content"
  limitation to be enforced "via the guarded tool boundary's ... policy" — the spec
  places this in the deterministic-guarantee tier, not the agent-judgment tier.
  Neither of ADR-015's two existing modes expresses it: `read-write` permits any
  content change to an existing file, `create-only` forbids touching an existing file
  at all. A mechanical body-preservation check (does the content after the closing
  `---` differ) is exactly the kind of harness mechanic Principle V permits — it
  judges *structure* (did the body change), never *content* (is this a good tag).
  This repo's established convention for revising an already-`accepted` ADR is a new,
  numbered ADR with a pointer added to the old one (ADR-011 → ADR-014, then ADR-011 →
  ADR-015 again for a different section) — never editing the accepted ADR's decision
  body in place. ADR-016 follows that convention.
- **Alternatives considered**: leaving policy at plain `read-write` and treating body
  preservation as an instruction-file-only guarantee, verified by evaluation sampling
  (rejected — this would silently downgrade a criterion the spec explicitly frames as
  a 100% deterministic guarantee, exactly the kind of erosion Constitution Principle II
  warns against, just aimed the opposite direction from its more common failure mode);
  a dedicated `update_frontmatter` structured tool instead of whole-file `write_file`
  (rejected — a new tool with harness-side re-serialization would put the harness in
  the business of composing page content, a Principle V risk, and is a much larger
  change than a mechanical string-split check); a general-purpose diff/patch tool
  (rejected — no other agent or spec need motivates that generality; disproportionate
  to the one guarantee actually required).

## R2: Frontmatter/body split is lexical, not a YAML parse

- **Decision**: The check splits on the literal first two `---`-only lines (open,
  close); everything after the second is "body." No YAML library is introduced.
- **Rationale**: Every page in this wiki is already required to open with exactly
  that two-delimiter shape (`data/agents/ingest/system-prompt.md`'s frontmatter
  convention, unchanged by this feature). A lexical check is sufficient, has no new
  dependency, and stays inside the dependency-light `Grimoire.AgentRuntime.Guardrails`
  layer. It also fails closed correctly: a document that doesn't have the expected
  two-delimiter shape can't be verified, so the check denies rather than guesses.
- **Alternatives considered**: parsing YAML and comparing the remaining Markdown AST
  (rejected — adds a parsing dependency to a layer that currently has none, for a
  guarantee a plain string split already gives correctly given the wiki's own
  frontmatter convention).

## R3: Dispatch — immediate rejection (Query's shape), not a queue (Ingest's shape)

- **Decision**: `LintRunCoordinator` copies `QueryRunCoordinator`'s
  `SemaphoreSlim.WaitAsync(0, ...)` immediate-rejection shape, fixed at limit 1 — not
  `IngestRunCoordinator`'s persisted single-slot FIFO queue.
- **Rationale**: FR-003's literal text — "a trigger while one is active MUST be
  rejected immediately with a clear message" — and the spec's own edge case ("a
  second trigger while one is active is rejected with a clear 'busy' message")
  describe rejection, not queuing. `IngestRunCoordinator.EnqueueAsync` always accepts
  and persists a queued row (`OperationalStateRepository`, SQLite) — it never rejects
  a submission. `QueryRunCoordinator`'s zero-timeout semaphore acquire returns a
  rejection result immediately with no persisted state at all when the limit is
  reached — precisely FR-003's shape, with the limit fixed at 1 instead of Query's
  configurable 3. No queue table, no `ResumeAsync`/pause-on-restart logic is needed:
  a rejected trigger simply never started, so there is nothing to resume.
- **Alternatives considered**: reusing `IngestRunCoordinator`'s queue and just setting
  its slot count to 1 (rejected — a queued-and-later-run trigger is not "rejected
  immediately," and the spec explicitly wants wiki-wide analysis of a moving target to
  have "no benefit from parallelism," not "run it later automatically" — the user must
  re-trigger deliberately once they see the busy message).

## R4: Findings Report — Hub-written, sentinel-safe format (Conversation Record's shape), not agent-written (Task Artifact's shape)

- **Decision**: The Findings Report is written entirely by the Hub, at the run's
  terminal event, packaging the agent's final narrative into a new,
  purpose-built, sentinel-safe file format — following `ConversationRecordFormat`'s
  hardening precedent, not `TaskArtifactStore`'s simpler agent-written YAML+body shape.
- **Rationale**: A Findings Report's content — finding descriptions, affected pages,
  proposed remediations — is entirely agent-authored prose produced from an autonomous
  whole-wiki read, exactly the kind of untrusted-content-adjacent authorship
  `ConversationRecordFormat` was hardened against (its own design point: agent output
  must not be able to forge the record's structural sentinels). Task Artifacts don't
  need the same hardening because most of their bookkeeping is harness-computed, with
  only one free-text field appended once at the end. A Findings Report is written
  exactly once per run (never appended to across runs — each run gets its own file),
  so it needs the *sentinel-safety* discipline of the Conversation Record format but
  not its *append* machinery (no per-conversation lock, no in-memory context cache,
  no multi-block accumulation).
- **Alternatives considered**: agent-written, `TaskArtifactStore`-shaped (rejected —
  under-hardened against agent-authored content forging report structure, given the
  report's entire content is exactly that kind of output, unlike ingest's mostly
  harness-computed artifact); reusing `ConversationRecordFormat` literally (rejected —
  its shape is turns/prompts/answers, structurally unrelated to
  categories/findings/affected-pages/remediations; a new format sharing its *safety
  discipline*, not its *schema*, is the right level of reuse).

## R5: Findings content arrives via the agent's final narrative, not a new tool

- **Decision**: Lint's system prompt instructs it to produce one large structured
  final response (its narrative); the Hub packages that narrative into the Findings
  Report at the terminal event — the same mechanism `QueryRunCoordinator` already uses
  to package `loopResult.Narrative`/streamed answer text into a Conversation Record
  turn.
- **Rationale**: No new tool is needed to "submit findings" — the agent loop already
  produces a final narrative text at `end_turn`; treating that text as the report body
  is exactly the same mechanical packaging step Query and Ingest already perform for
  their own final outputs. Reuses `RunEventEmitter.EmitCompleted(narrative, ...)`
  unchanged.
- **Alternatives considered**: a dedicated `submit_findings_report` tool taking
  structured JSON (rejected — forces the harness to define and validate a rigid
  findings schema, which would encode content-shape judgment — e.g. what counts as a
  category — back into backend code; the spec's Findings Report entity is
  intentionally prose-shaped, matching how Ingest and Query already report through
  free-text narrative).

## R6: `inbound_links`/`last_reviewed` are new frontmatter fields, owned by this feature

- **Decision**: Lint's system prompt introduces `inbound_links` (int) and
  `last_reviewed` (ISO-8601 date) as new optional frontmatter fields, maintained going
  forward only by Lint — Ingest's system prompt is not changed to write them.
- **Rationale**: `grep -rn "inbound_link\|last_reviewed" data/agents/ backend/` finds
  zero hits in the live, binding frontmatter spec — both fields exist today only in
  the historical, explicitly-non-binding `docs/llm-wiki-magrathea-skill.md`, which
  already names Lint as their sole maintainer ("`inbound_links` wird beim `/lint`-Lauf
  aktualisiert — nicht beim Ingest, zu teuer"). This feature formalizes exactly that
  historical intent; per the project's Document Map, the historical doc is source
  material only, never cited as a requirement, but it correctly predicted this
  feature's shape and is a useful reference when writing `agents/lint/system-prompt.md`.
- **Alternatives considered**: none — the spec (Terminology's "Inbound-Link Refresh")
  and this file both independently land on the same design the historical doc already
  described; no other option was seriously in tension with it.
