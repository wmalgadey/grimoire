# Research: Query Agent Synthesis Writes

Phase 0 output for `specs/012-query-synthesis-writes/spec.md`. Decisions below resolve
every open point the spec's Assumptions hand to planning, above all the superseding
architecture decision recorded as `docs/adr/ADR-015-query-write-scope-and-wiki-write-coordination.md`
(status `proposed`).

Code facts referenced here were verified against the current implementation:
`backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs`,
`backend/src/Grimoire.AgentRuntime/Guardrails/WriteJournal.cs`,
`backend/src/Grimoire.Domain/Guardrails/SafetyPolicy.cs` (+ `PolicyDecision.cs`),
`backend/src/Grimoire.AgentRuntime/Instructions/PolicyLoader.cs`,
`backend/src/Grimoire.QueryAgent/QueryToolRegistry.cs`,
`backend/src/Grimoire.Hub/QueryDispatch/QueryRunCoordinator.cs`,
`backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs`,
`backend/src/Grimoire.Hub/QueryConversations/ConversationRecordStore.cs`,
`backend/src/Grimoire.AgentRuntime/RunEvents/RunEventEmitter.cs`,
`data/agents/query/policy.json`, `data/agents/ingest/policy.json`,
`data/agents/ingest/system-prompt.md`.

## R1: Why a new coordination mechanism is required at all

- **Decision**: Introduce `SharedFileWriteGuard` inside `GuardedToolExecutor`
  (ADR-015), rather than relying on any existing mechanism.
- **Rationale**: Today's "single writer" property for the wiki is pure process-count
  discipline — `IngestRunCoordinator` never runs more than one Ingest process, so
  Ingest never races itself. `QueryRunCoordinator`'s counting semaphore (limit 3)
  allows up to three concurrent Query processes, but until this feature none of them
  write. `GuardedToolExecutor.ExecuteWriteFileAsync`'s temp-file-plus-rename write is
  atomic per file only; `WriteJournal` is in-memory and per-run. None of this
  prevents a classic read-modify-write lost update when two separate OS processes
  (a concurrent Query turn and an Ingest run, or two Query turns) both read
  `index.md`, both recompute a new version, and both write — the second write
  silently discards the first's addition. This is a genuinely new race the moment
  Query's registry gains `write_file`; feature 013 (Lint) will hit the same race
  updating existing page frontmatter. It must be fixed once, in the shared chokepoint
  both agents already pass through.
- **Alternatives considered**: routing all wiki writes through the Hub (rejected —
  breaks the ADR-002 contract that agents write their own working tree directly, a
  much larger change than this feature's scope); reducing Query's concurrency limit
  to 1 (rejected — regresses feature-008/011 user-facing concurrency for a problem
  that is local to a few shared files, not query throughput generally); a full
  transactional store replacing plain markdown files (rejected — no ADR or spec
  motivates abandoning the git-tracked plain-file domain-state model of ADR-003, and
  it is wildly disproportionate to the actual race window).

## R2: Compare-and-swap instead of a lock spanning the whole agentic turn

- **Decision**: `SharedFileWriteGuard` records the SHA-256 of every file the run
  reads via `read_file`; on `write_file` to an existing target, it compares the
  file's current on-disk hash to that recorded value under a short cross-process
  lock, and denies (`write_conflict_stale_read`) on mismatch instead of writing.
- **Rationale**: The race can only be closed completely by serializing the whole
  read → LLM-reasoning → write cycle, or by making the write self-checking against
  what was actually read. The first option ties lock hold time to LLM latency
  (seconds), directly threatening FR-010's streaming/interruption responsiveness
  guarantee for every synthesis-capable turn, not just ones that hit real
  contention. The second option (compare-and-swap) needs the lock only for the
  check-plus-atomic-rename itself — milliseconds — and turns a stale write into a
  normal, already-idiomatic tool-error the agent's own loop recovers from by
  re-reading and retrying, exactly like a policy denial. Genuine contention is rare
  by construction (bounded concurrency, narrow write scope), so the cost of this
  design is one occasional bounded retry, not a systemic slowdown.
- **Alternatives considered**: pessimistic per-run reservation of `index.md`/`log.md`
  for the run's full lifetime (rejected — run lifetime is LLM-latency-bound, would
  serialize unrelated turns against each other for no correctness benefit beyond
  what CAS already gives); Hub-mediated appends for just these two files (rejected —
  same ADR-002 boundary problem as R1's rejected option, and inconsistent with
  treating all guarded writes uniformly).

## R3: Create-only mode is a policy-schema addition, not new domain logic

- **Decision**: `data/agents/query/policy.json`'s `pages/` write rule gets
  `"mode": "create-only"`; `SafetyPolicy` (pure, dependency-free) returns whether the
  matched rule is create-only as part of its `PolicyDecision`, and
  `GuardedToolExecutor` (which already does I/O) performs the actual
  `File.Exists` check before writing.
- **Rationale**: `SafetyPolicy.Evaluate` is deliberately I/O-free (existing contract
  comment: "no I/O, no logging") — an existence check cannot live there without
  breaking that contract. Keeping the check as a plain existence test (not a
  "recognize this is a Synthesis Page" test) preserves Principle V exactly: the
  harness enforces *structure* (new file vs. pre-existing file), while *content*
  classification (is this actually a synthesis, does it belong under `pages/`) stays
  entirely with the agent under its instruction file.
- **Alternatives considered**: a dedicated `pages/synthesis/` subfolder as the only
  allowed write target (rejected — the spec's page-type conventions are an
  instruction-file concern; forcing a folder split from the harness side would
  encode a content-classification decision in backend policy). A boolean
  `createOnly` flag instead of a `mode` string (rejected only for extensibility —
  `mode` leaves room for a future third mode without a second boolean; functionally
  equivalent today).

## R4: Reporting created pages is mechanical, sourced from the existing journal

- **Decision**: `RunCompletionMetadata` (already the vehicle for
  `DeniedActions`/policy identity/model info reaching the Hub from a
  write-incapable-by-artifact agent, per its own doc comment) gains an optional
  `CreatedArtifacts` list, populated from `GuardedToolExecutor.TouchedPaths` filtered
  to paths matched by a create-only rule. The Hub writes this into the Conversation
  Record's already-open `created_pages:` bookkeeping key (ADR-014 reserved exactly
  this extension point).
- **Rationale**: `TouchedPaths` already exists and already records every successful
  write this run performed — reusing it means zero new tracking state, and the
  filter (create-only rule match) is the same structural fact already computed for
  the write-scope decision, not a re-derivation of content meaning.
- **Alternatives considered**: having the agent report created pages via its answer
  text and parsing that (rejected — fragile, and turns a mechanical fact into a
  natural-language-parsing problem); a dedicated new NDJSON event type
  (rejected — `completed` metadata already round-trips exactly this kind of
  structured, harness-known fact, per its existing design intent).

## R5: Lock storage location and naming

- **Decision**: New `ResolvedGrimoirePaths.WriteLocksDir`
  (`GrimoirePathOptions.DefaultWriteLocksDirName = "write-locks"`), resolved beneath
  `DataDir` via `GrimoirePathResolver`, auto-created like other writable-data
  locations. Lock files are named by the SHA-256 hex digest of the target's
  canonical absolute path (avoids any path-escaping/encoding concerns and keeps
  filenames constant-length).
- **Rationale**: Matches the existing `StateDbPath`/`ConversationsDir` pattern
  exactly (ADR-009 single composition point; ADR-003 operational state outside
  `wiki/` and git). Hashing the target path sidesteps filesystem-unsafe characters
  and nested-directory creation that mirroring the wiki's own path structure would
  require.
- **Alternatives considered**: a single global wiki-wide lock (rejected — would
  serialize unrelated new-page creations against each other for no reason, directly
  hurting FR-010 under any real concurrent load); sibling `.lock` files next to each
  target inside `wiki/` (rejected — pollutes git-tracked domain state with
  operational artifacts, violating ADR-003's split).

## R6: Multi-process testing is required to actually prove the guarantee

- **Decision**: `Test Strategy`'s SC-003 row requires, alongside hermetic
  in-process tests, at least one integration test that spawns two real separate
  `dotnet run` processes against the same temp wiki root and write-locks directory.
- **Rationale**: `FakeAgentProcess`-based tests exercise `GuardedToolExecutor` and
  `SharedFileWriteGuard` in-process; they can prove the CAS logic is correct but
  cannot prove the underlying `FileStream`/`FileShare.None` primitive actually
  provides cross-process exclusion on the target OS/CI runner. Constitution
  Principle II requires hermetic tests for harness contracts, but "hermetic" means
  "no live LLM calls or credentials," not "no real OS processes" — spawning two
  local test-harness processes with no network dependency remains fully hermetic.
- **Alternatives considered**: trusting `.NET`'s documented `FileShare.None`
  semantics without a real multi-process test (rejected — this is exactly the kind
  of platform-boundary assumption Principle II's "not mocked doubles" spirit warns
  against for a genuinely new structural guarantee).
