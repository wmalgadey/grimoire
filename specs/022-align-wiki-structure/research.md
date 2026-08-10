# Phase 0 Research: Wiki Structure Truth

**Feature**: 022-align-wiki-structure | **Date**: 2026-08-10

Every unknown in the Technical Context is resolved below. No `NEEDS CLARIFICATION` remains.

---

## R1 — Why the query agent reported a populated wiki as empty

**Decision**: The cause is three defects in five lines of
`Grimoire.QueryAgent/Instructions/system-prompt.md`, not a harness or policy fault.

The prompt's Path convention block says: resolve wikilinks against `pages/<slug>.md`; "Only
`pages/`, `index.md`, and `log.md` are readable — `list_files(".")` on the bare root is not
allowed"; "use `list_files("pages/")` to see every available page instead of guessing a path."

- The wrapper folder was retired by feature 014 and cannot exist.
- The claim that root listing is forbidden is **false**. `policy.json` grants
  `{"pathPrefix": "."}` on read; `PolicyLoader.NormalizeRulePrefix` maps `"."` to the wiki root
  with a trailing separator; `SafetyPolicy.PrefixMatches` resolves that via `StartsWith`, and
  its second branch matches the bare directory too. `GuardedToolExecutor.ExecuteListFilesAsync`
  evaluates the same read policy. `list_files(".")` is allowed today for all three agents.
- The one navigation instruction the agent was given points at nothing.

**Rationale**: The agent followed its instructions correctly and reached a false conclusion.
Nothing in the harness needs fixing for FR-004/FR-005; the instruction does.

**Alternatives considered**: Widening the read policy (unnecessary — already wide); adding a
harness-side "wiki state" tool (rejected — ADR-006 fixes the tool surface at three, and wiki
state description is agent judgment under Principle V).

---

## R2 — What a bare wikilink resolves to now

**Decision**: By **filename, folder-agnostic** — only the final path segment is significant, so
`[[slug]]` and `[[category/slug]]` name the same target. The agent resolves it against the
article filenames it enumerated, rather than constructing a path.

**Rationale**: Three independent sources agree.

1. The ingest prompt already states it: "use the bare page slug, not the folder path
   (Obsidian-style resolution works by filename regardless of which folder the page lives in)."
2. The harness's own mechanical definition, `LintDeterministicScorers.cs:262` —
   `var targetSlug = match.Groups[1].Value.Split('/')[^1];` — over
   `Directory.GetFiles(run.WikiRoot, "*.md", SearchOption.AllDirectories)` keyed by
   `Path.GetFileNameWithoutExtension`.
3. Feature 014 made category folders open-ended, so no fixed prefix could be reconstructed even
   in principle.

There is no wikilink resolver in production harness code at all — only in eval scorers — so the
instruction wording *is* the entire contract.

**Alternatives considered**: Mandating folder-qualified wikilinks everywhere (rejected — would
invalidate existing wiki content and contradicts the ingest prompt's existing rule).

---

## R3 — The catalog-link contradiction inside the ingest prompt

**Decision**: `index.md` catalog lines use **content-root-relative markdown links**; wikilinks
are used everywhere else. The ingest prompt's "Deviation from OKF" paragraph must carve out
`index.md` explicitly.

**Rationale**: The prompt currently says "use a wikilink for every reference to another wiki
page — in body prose, frontmatter values, **the index**, and the log alike — never
`[title](path)`", and then, 160 lines later, mandates the opposite for catalog entries. ADR-017
settles it structurally: `SharedFileWriteGuard` denies `catalog_entry_malformed` for any new
`- [`-led line failing `^- \[.+\]\(.+\) — .+ — .+$`, which a wikilink line cannot satisfy. The
guard wins; the Deviation paragraph is the error.

**Alternatives considered**: Relaxing the ADR-017 regex to accept wikilinks (rejected — it would
weaken an accepted ADR's structural guarantee to fix a prompt typo).

---

## R4 — Where the operator's grant set lives

**Decision**: A new top-level `Grimoire:HarnessSurfaceReads` section in `appsettings.json`,
bound to a `HarnessSurfaceReadOptions` record with four booleans defaulting to `false`. Not in
`Grimoire:Paths`, not in `policy.json`, and with no CLI switch.

**Rationale**: ADR-022 prescribes exactly this shape — "Adding a new runtime location means
adding a `GrimoirePathOptions` field and an `appsettings.json` key — never a switch" — and caps
CLI *path* switches at three, structurally enforced by `DirectorySwitchSurfaceRuleTests`. Two
precedents already exist for non-path operator settings on a top-level `Grimoire:` key:
`QueryConcurrencyOptions` and `LintReviewWindowOptions`, the latter a complete working template
from Hub binding through CLI argument to agent parsing.

`policy.json` is disqualified on four independent grounds: the read schema cannot express
exclusion at all; the file is a build-distributed developer source that every build overwrites,
so operator edits are lost; three separate files would have to stay in sync for a decision the
spec says applies uniformly; and changing it alters the recorded policy SHA-256, which ADR-012
fingerprints — invalidating the eval recording corpus on every grant flip.

`Grimoire:Paths` is disqualified because a grant set is not a runtime location and would corrupt
`ResolvedGrimoirePaths`' invariant.

**Alternatives considered**: Hub-side filtering of tool results (rejected — the denial would be
invisible to the agent and unrecorded, contradicting FR-016); moving the harness surfaces back
out of the wiki root (rejected — reverses ADR-022's clarified product decision and the user
explicitly wants the surfaces where they can see them).

---

## R5 — How the denial is enforced

**Decision**: A subtractive denied-read-subtree set on `SafetyPolicy`, checked in the read branch
of `Evaluate` **before** the allow loop, returning a new reason `harness_surface_not_granted`.
Subtrees match as directories including the bare directory itself.

**Rationale**: Read scope is `IReadOnlyList<string> _readPrefixes` — no mode, no exclusions —
and `WriteRule.ExcludePrefixes` is write-only in `Evaluate` and exact-match in `IsExcluded`, so
it cannot exclude a subtree. The precedent for a runtime narrowing that leaves the loaded policy
identity intact is `WithNoWriteAccess()`, already used for Lint's read-only message-turn mode.

A distinct reason rather than reusing `no_rule` is required by SC-010, because the reason string
is echoed to the agent in the tool result and the operator must be able to tell "not granted"
from "outside scope". Matching the bare directory matters so `list_files("tasks")` is denied,
not only `read_file("tasks/x.md")`.

`SafetyPolicy` receives plain strings — the boolean-to-subtree mapping happens in agent
composition, keeping the Domain dependency-free.

**Alternatives considered**: A new `WriteMode`-style enum on read rules (rejected — over-general
for a fixed set of four reserved names); denying at `GuardedToolExecutor` instead of in the
policy (rejected — splits the authorization decision across two layers and bypasses the
`Evaluate` funnel every other check goes through).

---

## R6 — Whether denying `remediation-tasks/` breaks remediation messaging

**Decision**: It does not. No change is needed to preserve ADR-018's message-turn mode.

**Rationale**: The Lint agent receives remediation context as Hub-injected CLI arguments
(`--proposal-description`, `--attached-context`), read from the record by the Hub — not through
a guarded `read_file`. Likewise ADR-014's conversation-context assembly is a Hub-side read. The
guarded boundary governs only what the agent reaches on its own.

This produces an intentional asymmetry worth stating explicitly: the harness may put a surface's
content in front of the agent while the guarded tool denies the agent reading that surface
itself. ADR-023 records it.

**Alternatives considered**: Auto-granting `remediation-tasks/` whenever a remediation run is
dispatched (rejected — unnecessary, and it would make the effective scope depend on run kind,
defeating FR-017's reconstructability).

---

## R7 — Whether `index.md`/`log.md` creation needs a guard change

**Decision**: No. FR-013 is an instruction change plus a test.

**Rationale**: ADR-017's `log.md` check already reads "Deny `log_entry_not_appended` unless the
proposed content starts with the current on-disk content byte-for-byte **(or the file does not
yet exist and this is the first write)**." The `index.md` check only inspects `- [` lines
present in the proposal and absent from current content, so a missing file is naturally fine.
Both filenames are exact-match write rules in all three shipped policies.

**Alternatives considered**: A Hub bootstrap step creating both files at content-root resolution
(rejected — it would move a wiki-content decision into the harness, and the spec clarification
explicitly chose the agent).

---

## R8 — Rewriting the structural rule

**Decision**: Split into two classes — `RetiredPagesWrapperPathRuleTests` (the path concept) and
`WikiContentTerminologyRuleTests` (the term) — sharing a new `ArchScan.FindRepositoryRoot()`.

**Rationale**: The two concerns have different scan surfaces, different tokenizers, and
different exemption models; one `[Fact]` over both would need a single tokenizer spanning C# and
Markdown and would produce ambiguous failures.

The existing rule's tolerance is one predicate — `IsPagesPathSegment`, which matches only
`"pages"` exactly, `pages/`, `/pages`, or `--pages-dir`, with a doc comment naming
`pages_touched` and `wiki.ingest.pages_touched_total` as *deliberately* not misreported. The
terminology rule inverts precisely that. Its `PagesDirSymbolPattern` and
`AllowedRelativeFilePaths` are both dead — `PagesDir` no longer appears anywhere in
`backend/src` — and can be deleted.

`FindBackendSrcDirectory` already walks up to the repo root and returns a child; refactoring it
into `FindRepositoryRoot()` reaches `docs/` fine, and `AgentArtifactNamingRuleTests` already
does the same walk for `docs/conventions/`.

**Exemptions**: two tiers. Path-glob exemptions carry a justification mirrored into
`docs/conventions/wiki-content-root.md`, checked by a second `[Fact]` that fails on drift in
either direction — the pattern `AgentArtifactNamingRuleTests.ExemptionFixture_MustMirror_TheConventionDocument`
already establishes. Plus an inline historical-marker rule: a line under `docs/adr/` or
`specs/` is exempt when it or its immediate context carries a retirement marker. ADR-015:115 and
ADR-016:85–87 already read "the `pages/` wrapper is retired" adjacent to their offending lines,
so this exempts them **without editing accepted records**, which is what SC-004 demands.

**Mandatory enumerator exclusions**: `bin/`, `obj/`, `node_modules`, `.svelte-kit`, `.git`,
`.grimoire` (build output of `PublishAgentRuntime` — scanning it double-reports every
instruction violation), `frontend/` wholesale (SvelteKit's `+page.svelte` is framework naming,
not wiki terminology), and `Grimoire.AgentEvals/Fixtures/recordings/**` (frozen ADR-012
transcripts).

**Red/Green probe**: extract the scan into `Scan(IEnumerable<ScanTarget>)` where a target is
`(RelativePath, Text)` supplied rather than read from disk, then make the probe a **permanent**
second `[Fact]` feeding synthetic violating text. That satisfies Principle III's "prove it
detects" without mutating the repo, and a third `[Fact]` feeding a marker-carrying ADR fragment
pins FR-010. The manual ceremony `tasks.md` records is: introduce `list_files("pages/")` into
the query prompt, run the ArchTests project, confirm the failure names that file, revert.

**Sequencing trap**: the rule goes red against real files the moment it lands, because the sweep
has not happened yet. Phase 0 writes the rule (red is expected and documented); the sweep phase
turns it green. Feature 014's own class documents this same pattern.

**Alternatives considered**: Extending the existing C# tokenizer to Markdown (rejected — the
tokenizer is already flagged at CC 39 in `docs/code-complexity-analysis.md`; a per-file-type
scanner is simpler); exempting `docs/adr/` and `specs/` wholesale (rejected — blinds the rule to
the drift it exists to catch).

---

## R9 — The eval re-capture obligation

**Decision**: All ingest, query and lint recordings under
`backend/tests/Grimoire.AgentEvals/Fixtures/recordings/` are re-captured in this feature.

**Rationale**: ADR-012 makes manifest fingerprints — instruction surface per ADR-007, policy per
ADR-006 — the staleness authority, and states that "replay tests failing on staleness in the
standard PR pipeline are the merge gate for instruction changes." `.github/workflows/ci.yml`
confirms it operationally: the replay step greps for `Skipped: 0,` and fails otherwise, with the
comment "A stale/missing recording fails here — that failure IS the FR-016 merge gate for
instruction-file changes."

This feature rewrites all three system prompts. Every recording for every agent goes stale at
once. `EvalPaths` resolves instructions from the agent project sources, so the eval suite reads
exactly the files being edited with no build step in between.

**Alternatives considered**: None available — bypassing the gate would require disabling the
zero-skip check, which is itself a spec'd guarantee of feature 009.

---

## R10 — Scope of the terminology rename

**Decision**: "Article" is canonical. The rename covers identifiers, serialized names, metric
names, and prose. It does **not** cover: frontmatter `type:` values (`Concept`, `Technology`,
…), machine-read transport keys (`targetPath`, `inbound_links`, `superseded_by`, `supersedes`,
`confidence`, `last_reviewed`), denial reason constants, or SvelteKit framework filenames.

**Rationale**: `type:` values are data already written into existing wiki files; renaming them
would rewrite content this feature explicitly does not touch (FR-012). Transport keys and denial
reasons are harness contracts whose names carry no wiki-content meaning.

**Verified facts bounding the rename**:

- **No SQLite table or column contains the term.** DDL at `OperationalStateRepository.cs:66-85`
  creates `operational_task_state`, `ingest_queue`, `hub_flags`, `remediation_tasks`.
- **No CLI argument contains the term** — a scan for `"--*page*"` in `backend/src` returns
  nothing. So the Hub↔agent process contract is unaffected except for the NDJSON event property.
- **Wire/on-disk names that do change**: the NDJSON `createdPages` property
  (`AgentRunEvent.cs:54`); the conversation-record key `created_pages`
  (`ConversationRecordFormat.cs:113,117,496`); task-artifact frontmatter keys `pages_touched`,
  `pages_created`, `pages_updated`, `pages_superseded` (`TaskArtifactStore.cs`,
  `HubTaskArtifactWriter.cs:61-64`, `RestartReconciler.cs:112`).
- **Renaming `created_pages` is safe on existing records**: the conversation parser's `default:`
  branch tolerates unknown keys explicitly ("forward compatibility, e.g. feature 012's
  `created_pages`"), so a legacy record's old key is ignored rather than triggering the
  fail-closed `conversation_record_unreadable` path. This must be covered by a test, since it is
  the one place the clean break could otherwise surface as a runtime failure.
- **Signals declared as contract rows in earlier features' plans**:
  `wiki.ingest.pages_touched_total` (001, 002), the `ingest.agent.completed` fields
  `pages_created`/`pages_updated`/`pages_superseded` (002), `wiki.query.synthesis_pages_created_total`
  and the `created_pages:` key (012). Per the Constitution's non-retroactivity clause those
  features are not rendered non-compliant; 022 declares the renamed signals as its own contract
  rows with its own tests.
- **ADR-015 already disagrees with the shipped code**: it names the event field
  `createdArtifacts` while `AgentRunEvent.cs:54` ships `createdPages`. The rename resolves the
  divergence rather than perpetuating it.

**Alternatives considered**: Renaming prose only and leaving identifiers (rejected — the user
chose the full rename explicitly, and identifier names are what the arch rule can actually
enforce); a dual-name alias period (rejected — pre-1.0, no deployment to preserve).

---

## R11 — ADR amendment scope

**Decision**: Amend nine ADRs with inline notes. Keep five stale-looking passages untouched as
historical record.

**Keep — historical, must pass the rule unmodified (FR-010/SC-004)**: ADR-006:43 (rejected
option naming `create_wiki_page`); ADR-015:107–123 and ADR-016:85–89 (both already carry inline
"the `pages/` wrapper is retired" notes); ADR-016:63 (rejected option); ADR-009:98 (consequence
of a completed migration).

**Amend — stale current-state description, not history**:

| ADR | What is wrong |
|-----|---------------|
| ADR-011:108–111 | States Query's read scope is `pages/`, `index.md`, `log.md` and that it has "no write section". **Both false today** — the shipped policy reads `[index.md, log.md, "."]` and has a full create-only write section. Highest priority. |
| ADR-018:125, ADR-014:99, ADR-015:221 | Name live event/record fields being renamed |
| ADR-015:214 | Section heading naming a live field |
| ADR-003:9,40 | "wiki pages" as the description of what domain state *is* |
| ADR-009:74 | "anchor for relative page paths" — live mechanism description |
| ADR-016:155 | Terminology plus a load-bearing cross-reference into a file being rewritten |
| ADR-006:86 | "large-page edits" in a Consequence |

**Rationale**: FR-010 exempts records that "document the retirement as a past decision" — not
every appearance of the term in an accepted ADR. A sentence describing what the system is *now*
is in FR-008's scope regardless of which file it sits in.

**"Synthesis Page" → "Synthesis Article"**: renamed. It is a defined term with no persisted
representation, appearing in the query prompt, ADR-015 and ADR-016, and it feeds the metric and
log-event names already in the rename set.

---

## R12 — Observability wiring

**Decision**: Contract tests attach `AddInMemoryExporter` through
`TelemetryExtensions.AddHubTelemetry(services, configureTracing)`.

**Rationale**: That parameter exists for exactly this purpose, and its own documentation says so:
"lets tests attach an additional exporter to the same `TracerProviderBuilder` the app uses, so
tests observe span export decisions made under the real sampler/instrumentation instead of a
test-only always-record listener." This is what the Constitution's 2026-08-09 production-wiring
amendment requires, and the mechanism that closes the feature-003 failure mode.

Tests registering a process-wide `ActivityListener` must join the
`HubActivityListenerObservability` or `IngestAgentObservabilityListeners` collection
(`DisableParallelization`) — feature 019 documents the concrete race, including ASP.NET Core
hosting instrumentation wrongly parenting spans that assert they are root.

**Naming convention in force**: agent-side metrics `wiki.<agent>.<noun>_total`; Hub metrics
`hub.<area>.<noun>_total`; agent spans `<agent>_agent.<verb_noun>`; Hub spans
`hub.<area>.<verb_noun>`. New signals follow it.

**CI**: `.github/workflows/ci.yml` already runs ArchTests, Domain unit tests, IntegrationTests,
and the zero-skip replay evals. No new pipeline step is needed — but Principle IV still requires
an explicit CI-enforcement task per contract row.
