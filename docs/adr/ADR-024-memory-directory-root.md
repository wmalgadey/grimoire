---
status: Accepted
---

# ADR-024: Memory Directory — A Fourth Independent Root for Agent Process Bookkeeping

> **Superseded in part by [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md)
> — Structural Enforcement section only**: M1, M2, and M4 keep their substance
> unchanged and this ADR remains `Accepted` and governing for everything else it
> decided; only the Structural Enforcement section's enforcement-mechanism claim is
> replaced outright (reflection/IL tests → classicist behavioral tests), and the
> "Principle III escape valve" justification it cited is withdrawn — no such clause
> exists in any version of the constitution. The Structural Enforcement table's
> references to `DirectorySwitchSurfaceRuleTests`/`NoCodeLevelPathDefaultsRuleTests`/
> `PathOptionsGroupingRuleTests` (now retired) are historical per ADR immutability —
> ADR-032 names the current enforcement for each rule.
>
> **Amends [ADR-022](ADR-022-minimal-directory-configuration-surface.md)**: rule R1's
> switch cap grows from three named entries to four (`--memory-dir` added); the
> root/sub-path table gains `MemoryDir` and re-anchors `TasksDir`/`ConversationsDir`/
> `FindingsDir`/`RemediationTasksDir` from `WikiDir` to `MemoryDir`, reversing the
> placement ADR-022 recorded as deliberate. Rule R2 gains a namespace-scoped companion
> (M2) rather than a new global literal. Everything else ADR-022 decided — three-tier
> precedence, mandatory configuration file, no code defaults, agent-build distribution,
> one launch mode — is unchanged.

## Context and Problem Statement

ADR-022 settled the hub's directory surface at three independent, cwd-anchored roots
(`DataDir`, `WikiDir`, `AgentDir`) and capped the command-line switch surface at exactly
three entries so it could not regrow the way it did under ADR-009. As part of that same
decision it moved four bookkeeping locations — `TasksDir`, `ConversationsDir`,
`FindingsDir`, `RemediationTasksDir` — out of the git-ignored data directory and under
`WikiDir`, recording the move in its own consequences as:

> **Bad / deliberate**: conversations, findings, and remediation-task records move from
> the git-ignored data directory into the wiki directory (spec clarification 2026-08-06 —
> they are agent output). This reverses the ADR-003/ADR-009 placement of that bookkeeping
> as internal, git-ignored state: an operator who version-controls the wiki will now see
> it. Accepted as the clarified product decision; the wiki directory carries its own
> `.gitignore` if an operator wants them excluded.

That escape hatch has not held up. The wiki directory now mixes two operationally
different things in one tree: the maintained knowledge base (the product) and harness
record-keeping of what the agents did (process exhaust). An operator who wants to back
up, retain, or exclude one but not the other has no single location to point at — they
must enumerate four sub-paths, and the `.gitignore` remedy only covers the git case, not
backup jobs, retention policies, or storage placement. Feature 022
(`specs/022-memory-directory-root/spec.md`) asks for one directory that holds all four.

Four things make this an ADR rather than a decision inside the feature:

1. **The switch cap is structural.** ADR-022 rule R1 asserts `PathSwitchCatalog.All`
   contains *exactly three* named entries and `HubPathSettings` declares one
   `[CommandOption]` per entry, enforced by `DirectorySwitchSurfaceRuleTests` with a
   Red/Green probe. Spec FR-001 puts the memory folder "at the same configuration tier as"
   the other three roots. A fourth switch fails the build until R1 is amended — which is
   exactly what the rule is for, and exactly why it must be amended deliberately rather
   than edited around.
2. **Two ADRs name the on-disk home of records they own.** ADR-014 states Conversation
   Records live at `<base>/conversations/<conversationId>.md`; ADR-018 introduces
   `RemediationTasksDir`. Both must be re-anchored by decision, not by side effect.
3. **The move breaks a wiki-relative link contract.** The Ingest system prompt instructs
   the agent to write `Task: [[tasks/<task_id>.md]]` into `log.md`, and
   `RestartReconciler` hardcodes the same wikilink on the crash-recovery path. Once
   `tasks/` leaves the wiki tree, that link is dangling by construction — and the
   harness's own log-deduplication backstop is coupled to whatever the prompt tells the
   agent to write.
4. **The configuration file cannot express the change.** `Grimoire:Paths` is a flat list
   of eleven keys whose anchoring relationships exist only in code and in a comment block.
   Re-anchoring four of them from `WikiDir` to `MemoryDir` changes no key name and no
   value — the entire semantic change would be invisible in the file's diff, with only a
   comment moving. ADR-022 made `appsettings.json` the sole source of defaults precisely
   so "the full effective layout is readable in one versioned file"; a flat list under a
   four-root layout no longer delivers that.

## Decision Drivers

- Spec FR-001/FR-003/FR-004/SC-002: a fourth root, independent in both directions — no
  root's relocation may move another. This is the same independence contract ADR-022
  established for the first three, extended, not weakened.
- Spec FR-005/FR-006/SC-003/SC-004: per-option precedence CLI > environment >
  configuration file, with `appsettings.json` as the sole default source and a named
  startup failure when the key is absent. No code-level fallback (ADR-022 W1, unchanged).
- Spec FR-002/FR-010: the four bookkeeping locations move root, and *only* root — their
  internal file naming and per-record layout are untouched.
- Spec FR-011: no automatic migration, consistent with the precedent ADR-022 set for its
  own breaking layout change.
- Spec FR-012/SC-008: instruction files must stop describing these folders as reachable
  within the wiki tree, because they no longer are.
- ADR-022's anti-regrowth intent must survive: the cap is what stops sub-paths from
  leaking back into the switch surface, and it must remain an exact enumeration rather
  than becoming a count nobody defends.
- Constitution Principles III/IV: every structural rule needs a Red/Green-probed test, a
  CI gate, and — per Constitution v1.11.0 — a classification as a Boundary Rule or a
  Feature-Scoped Invariant in this document's Decision Outcome (see Structural
  Enforcement below). Principle V: which folders an agent treats as reachable is
  instruction content, so the correction lands in instruction files, not in backend code.
- Author directive (2026-08-11): the configuration file must reflect the directory
  structure. It is currently a flat list, but in reality the folders are grouped and
  scoped to a specific base directory, and that must be visible in `appsettings.json`.
- ADR-022's own stated goal for the configuration file — "the full effective layout is
  readable in one versioned file" — is the standard the shape is measured against, and a
  flat list stops meeting it once there are four roots and seven anchored sub-paths.
- Pre-1.0 (alpha) posture: Grimoire has no external installation carrying a
  pre-regrouping configuration-key name, which bears on the superseded-configuration-key
  option below.

## Considered Options

### Root shape

- **M-A: A fourth cwd-anchored root, `MemoryDir`, with its own `--memory-dir` switch.**
  Default `memory`, a sibling of the other three defaults. The four bookkeeping sub-paths
  re-anchor beneath it.
- **M-B: A fourth root configurable only through `appsettings.json`, no CLI switch.**
  Rejected. It is compatible with R1 as written, which is its only virtue. ADR-022's own
  criterion for what earns a switch is *operator-meaningful and independently
  relocatable* — the eleven switches it deleted were internal layout details, and the
  three it kept were roots. The memory directory is squarely in the kept category: US1's
  operator relocates it precisely to put bookkeeping on different storage. Making the one
  root an operator actually wants to redirect the only root they cannot redirect from the
  command line inverts the surface ADR-022 was tuning.
- **M-C: Keep the four sub-paths anchored at `WikiDir` and let operators override each to
  an absolute path.** Rejected: this is already possible today and is the status quo the
  spec rejects — four keys to set consistently, no single thing to name in a backup or
  retention rule, and defaults that still nest inside the wiki.
- **M-D: Nest the memory directory under `DataDir` (`<DataDir>/memory`).** Rejected: it
  reintroduces exactly the shared-parent coupling ADR-022 removed with `BaseDir` —
  relocating runtime data would drag bookkeeping along, violating FR-003.

### Which sub-paths move

- **N-A: All four (`TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir`)
  re-anchor at `MemoryDir`.**
- **N-B: Move conversations, findings and remediation tasks; leave `TasksDir` under the
  wiki** because the task artifact is the one record the wiki's `log.md` links to.
  Rejected: FR-002 names all four, the task artifact is the clearest instance of process
  bookkeeping in the system, and preserving one exception would keep the wiki tree mixed
  for no gain. The link is the thing that should change, not the placement (see L-A).

### The wiki-relative task link

- **L-A: Replace the wikilink with a bare task-id reference** (`Task: <task_id>`) in the
  Ingest system prompt and in `RestartReconciler`'s crash-recovery entry.
- **L-B: Keep a wikilink pointing outside the wiki root** (e.g. `[[../memory/tasks/…]]`).
  Rejected: a wikilink resolves within the wiki tree by definition. A link that escapes
  the root is meaningless to a wiki reader, unresolvable by the guarded tool surface, and
  would encode the memory directory's *relative position* — which is operator-configurable
  and therefore unknowable from inside a page.
- **L-C: Drop the task reference from the log paragraph entirely.** Rejected:
  `WikiLogAppender.EnsureLogEntryAsync` decides whether to append a harness backstop entry
  by testing whether `log.md` already contains the correlation id as an ordinal substring.
  Remove the id and every successful ingest run appends a spurious "(harness backstop)"
  entry plus a WARN and a counter increment — silently, because no test couples the prompt
  to the dedup. The Query agent already demonstrates this failure mode: its prompt never
  names the turn id, so its backstop fires on essentially every page-creating turn.

### Configuration-file shape

- **C-A: Group by anchoring root.** Four groups — `Data`, `Wiki`, `Agent`, `Memory` — each
  carrying a `Dir` key that is the root itself plus sibling keys for the sub-paths anchored
  to it. `SecretsFile` stays at the section level because it anchors at the working
  directory and belongs to no group. The JSON tree becomes the anchoring graph.
- **C-B: Keep the flat list and rely on comments.** Rejected. The status-quo comment block
  is accurate today, but nothing checks it, and this very feature demonstrates the failure
  mode: re-anchoring four keys from `WikiDir` to `MemoryDir` leaves every key name and
  every value identical, so the file's diff would show only a moved comment while the
  meaning of four locations changed. Under C-A the four keys physically move from the
  `Wiki` group into the `Memory` group — the diff *is* the semantics.
- **C-C: A `Roots` section plus a `SubPaths` section in which each sub-path names its
  anchor as a value** (`"TasksDir": { "Anchor": "Memory", "Value": "tasks" }`). Rejected:
  it turns anchoring into operator-supplied data, inviting an anchor the resolver does not
  implement. Anchoring is a structural fact of the code; the file should mirror it, not
  parameterize it.
- **C-D: Stay flat but prefix the keys** (`DataRawDir`, `MemoryTasksDir`). Rejected: it
  encodes hierarchy in a naming convention that nothing enforces, and yields worse
  environment-variable names than real nesting does.

### Superseded configuration key detection

- **S-A: Detect every superseded flat configuration key at startup and fail naming its
  replacement.** Considered: the key rename would otherwise fail silently — an
  unrecognized configuration key is simply ignored by the configuration binder, unlike an
  unrecognized command-line switch (a parser error) — so an operator still exporting
  `Grimoire__Paths__DataDir` would get the shipped default with no signal. For a feature
  whose entire purpose is letting an operator place bookkeeping deliberately, quietly
  discarding a placement instruction is a real failure mode.
- **S-B: Accept the silent-fallback failure mode.** Chosen. Grimoire is pre-1.0 (alpha):
  there is no external installation carrying a pre-regrouping key name for a detector to
  protect. S-A would put a fixed eleven-entry legacy-key table into production code —
  permanent compatibility ballast enumerating dead key names, maintained against a
  scenario the project's own maturity stage rules out. An operator who still exports an
  old key name gets the same ordinary silent-ignore treatment configuration systems give
  any unrecognized key, discoverable by comparing the resolved location against their
  expectation rather than through a named startup failure — the same bounded, pre-1.0
  breaking-change treatment ADR-022 already gave its own layout change.

## Decision Outcome

**Chosen: M-A + N-A + L-A + C-A + S-B.**

### Four roots

`GrimoirePathOptions` gains one field. The tier table from ADR-022 becomes:

| Tier | Fields | Anchor | CLI switch |
| --- | --- | --- | --- |
| **Roots** | `DataDir` | process working directory | `--data-dir` |
| | `WikiDir` | process working directory | `--wiki-dir` |
| | `AgentDir` | process working directory | `--agent-dir` |
| | `MemoryDir` | process working directory | `--memory-dir` |
| **Sub-paths** | `RawDir`, `StateDb`, `WriteLocksDir` | `DataDir` | *none* |
| | `TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir` | **`MemoryDir`** | *none* |
| | `SecretsFile` | process working directory | *none* |

- `MemoryDir` is anchored at the process working directory, never beneath another root.
  Its shipped default is `memory`. Relocating any other root leaves it where it is, and
  relocating it leaves the others where they are (FR-003/FR-004).
- The four bookkeeping sub-paths keep their configured values (`tasks`, `conversations`,
  `findings`, `remediation-tasks`) and their internal per-record layout verbatim. Only
  their anchor changes (FR-010).
- `MemoryDir` is a `WritableData` location: auto-created when absent (FR-007), like
  `DataDir` and `WikiDir` and unlike `AgentDir`.
- Everything else in ADR-022 stands unchanged: one options record, one resolver, the
  mandatory configuration file as sole default source, per-option precedence, fail-fast
  validation naming the logical location, and one `paths_resolved` startup report — which
  gains `memory_dir` as a mandatory field (FR-008/SC-006).
- An operator who explicitly configures `MemoryDir` to equal or nest inside another root
  gets exactly that. The resolver anchors and validates; it does not police an operator's
  deliberate choice (spec edge case).

### The configuration file is grouped by anchoring root

`Grimoire:Paths` stops being a flat list of eleven keys and becomes four groups plus one
ungrouped key. Each group's own root is the key `Dir`; every other key in the group is a
sub-path anchored at that group's resolved `Dir`.

```jsonc
"Grimoire": {
  "Paths": {
    // Each group's "Dir" is a root, anchored at the process working directory.
    // Every sibling key inside a group anchors at that group's resolved "Dir".
    // The four roots are mutually independent: relocating one never moves another.
    "Data":   { "Dir": ".grimoire",      "RawDir": "raw",
                "StateDb": "state/operational-state.db", "WriteLocksDir": "write-locks" },
    "Wiki":   { "Dir": "llm-wiki" },
    "Agent":  { "Dir": ".grimoire/agents" },
    "Memory": { "Dir": "memory",         "TasksDir": "tasks",
                "ConversationsDir": "conversations", "FindingsDir": "findings",
                "RemediationTasksDir": "remediation-tasks" },
    // Anchored at the process working directory, in no group by design: relocating
    // any root must never separate an operator from their credentials.
    "SecretsFile": ".env"
  }
}
```

- `Wiki` and `Agent` carry only a `Dir` today. They are still written as groups rather
  than bare strings, so all four roots read in parallel and so a future sub-path under
  either is an added key rather than a breaking shape change. (`index.md` and `log.md` are
  already children of the wiki root; they are simply not configurable.)
- **Key names change accordingly**, and with them the switch mappings and environment
  variables:

  | Location | Configuration key | Environment variable | Switch |
  | --- | --- | --- | --- |
  | data root | `Grimoire:Paths:Data:Dir` | `Grimoire__Paths__Data__Dir` | `--data-dir` |
  | wiki root | `Grimoire:Paths:Wiki:Dir` | `Grimoire__Paths__Wiki__Dir` | `--wiki-dir` |
  | agent root | `Grimoire:Paths:Agent:Dir` | `Grimoire__Paths__Agent__Dir` | `--agent-dir` |
  | memory root | `Grimoire:Paths:Memory:Dir` | `Grimoire__Paths__Memory__Dir` | `--memory-dir` |
  | task artifacts | `Grimoire:Paths:Memory:TasksDir` | `Grimoire__Paths__Memory__TasksDir` | *none* |
  | raw intake | `Grimoire:Paths:Data:RawDir` | `Grimoire__Paths__Data__RawDir` | *none* |
  | secrets file | `Grimoire:Paths:SecretsFile` | `Grimoire__Paths__SecretsFile` | *none* |

  The switch names, the `PathLocation` names (`data_dir`, `memory_dir`, …) and the
  `paths_resolved` log fields are **unchanged**. `Memory:Dir` ↔ `--memory-dir` ↔
  `memory_dir` reads consistently across all three surfaces.

- `GrimoirePathOptions` mirrors the tree: four group properties (`Data`, `Wiki`, `Agent`,
  `Memory`) of dedicated option types, plus `SecretsFile`. It remains **one options record
  bound from one section at one composition point** — ADR-009's rule is about there being a
  single place, not about that place being flat. Each group property is initialized
  (`= new()`) so an entirely absent JSON group binds to an empty group rather than null;
  the leaf path values stay null, so the no-code-default rules (ADR-022 R2, M2 below) are
  untouched by those initializers.
- **The missing-key failure gets better, not just different.** The mandatory-root gate now
  reports full key paths — `Grimoire:Paths:Memory:Dir` instead of `MemoryDir` — so the
  error names something an operator can search for verbatim in the file. The
  `paths_configuration_missing` event's `missing_keys` field carries the same full paths.

### Superseded configuration keys are not detected

An unrecognized configuration key — file-based or environment-variable-based — is
silently ignored by the configuration binder, exactly as any other unrecognized key
would be (S-B above). No `paths_configuration_superseded` log/span event or
`configuration_superseded` metric label exists;
`grimoire.hub.path_resolution_failures_total{reason}`'s label set is `configuration_missing`,
`agent_directory_empty`, `location_invalid`. The legacy key list considered under S-A is
not implemented anywhere in production code.

### The switch cap grows by one root and stays an exact enumeration

ADR-022 rule R1 is amended from three entries to four. The mechanism is untouched: the
rule remains an exact, named enumeration with a 1:1 `HubPathSettings` parity assertion and
a Red/Green probe, so the anti-regrowth property that motivated it is preserved. ADR-022's
accompanying sentence — "Adding a new runtime location means adding a
`GrimoirePathOptions` field and an `appsettings.json` key — **never** a switch" — continues
to bind every *sub-path*. A new *root* remains an ADR-level decision, which is what this
document is.

### The task reference in `log.md` stops being a wikilink

- The Ingest system prompt's `Task: [[tasks/<task_id>.md]]` becomes a bare
  `Task: <task_id>` reference.
- `RestartReconciler`'s crash-recovery paragraph drops its hardcoded `[[tasks/{taskId}.md]]`
  for the same bare form.
- The harness backstop's behavior is unchanged: `WikiLogAppender` matches the correlation
  id as an ordinal substring, so a paragraph naming the bare id satisfies it byte-for-byte.
- ADR-017's format enforcement is unaffected: its two regexes constrain the `log.md`
  heading shape and the `index.md` catalog line shape, and impose no requirement on
  paragraph content.

### The harness record folders leave the agent's reachable tree

This is a consequence worth stating as a decision, because it converts a prompt-level
convention into a structural fact. Today all three policies grant read and write under a
`"."` catch-all anchored at the wiki root, so `tasks/`, `conversations/`, `findings/` and
`remediation-tasks/` are policy-reachable and appear in `list_files(".")`; only the system
prompts tell the agent to skip them. Once these folders anchor at `MemoryDir` they are
outside the guarded root entirely: `GuardedToolExecutor` cannot list or write them, and
the `"."` rule no longer covers them. The corresponding instruction-file guidance is
therefore removed rather than reworded (FR-012) — it describes a tree the agent can no
longer see.

The agent's *own* task-artifact write is unaffected. `TaskArtifactStore` writes through
direct file I/O on the path supplied by `--tasks-dir`, in the
`Grimoire.IngestAgent.TaskArtifact` namespace that
`IngestAgentGuardedWriteBoundaryRuleTests` already whitelists for exactly this purpose
(ADR-002: each process owns its own artifact I/O). It never passed through the guarded
tool boundary, so moving the directory outside the wiki root removes an incidental
overlap rather than breaking a path.

### Consequences

- Good: an operator has one location to name in a backup job, a retention policy, a
  storage placement decision, or an exclusion rule — which is the entire point of the
  feature. The wiki directory becomes wiki content and nothing else.
- Good: this restores ADR-003's intent (harness bookkeeping does not live in the
  knowledge base) without reverting ADR-022's clarification that these records are agent
  output rather than internal runtime state. They are neither hidden inside git-ignored
  operational state nor mixed into the wiki: they are their own operator-visible root.
- Good: the "these are not pages" contract stops being enforced solely by prompt text.
  The folders are no longer reachable through the guarded tool surface at all.
- Good: the anchoring graph becomes checkable rather than commented. Under the flat list,
  a sub-path's anchor was visible only by reading `GrimoirePathResolver`; a key could sit
  under a comment claiming one anchor while the resolver used another, and nothing would
  notice. Rule M5 below turns the grouping into an enforced invariant — a sub-path declared
  in the `Memory` group that the resolver anchors anywhere else fails the build. The
  grouping is load-bearing, not decorative.
- Good: no compatibility-ballast code — no fixed eleven-entry legacy-key table, no
  dedicated exception type, no dedicated log event — for a case the project's own pre-1.0
  status rules out (S-B).
- **Bad**: the configuration-key rename is a second breaking change on top of the root
  move, and it hits all four roots rather than only the new one. Every
  `Grimoire__Paths__DataDir`-style environment variable becomes
  `Grimoire__Paths__Data__Dir`. Accepted on ADR-022's pre-1.0 clean-break precedent, and
  bounded two ways: the CLI switches — the surface ADR-022 established as the
  operator-facing one — do not change at all, and operators who never set an environment
  variable see nothing. An operator who does still export an old-form environment
  variable gets the ordinary silent-ignore treatment (S-B), discoverable only by comparing
  the resolved location against their expectation, not a named startup failure.
- **Bad**: a breaking configuration change with no migration (FR-011). An operator with
  existing records under the wiki directory relocates them by hand. This is the same
  treatment ADR-022 gave its own layout change, and the same treatment the runtime data,
  wiki, and agent roots received.
- **Bad**: all three agent system prompts change, so every eval scenario's
  `system_prompt` fingerprint goes stale — all 22 scenarios and 252 recording files under
  `backend/tests/Grimoire.AgentEvals/Fixtures/recordings/`. PR CI runs the AgentEvals
  project unfiltered, so this is a hard merge gate, and recordings must be re-captured
  against a live provider before the feature can merge. This is ADR-012's staleness gate
  working exactly as designed, but it is a mandatory and non-trivial implementation step,
  not a formality.
- **Bad**: ADR-022 rule R2's tripwire cannot be extended verbatim to this root. R2 bans an
  IL string literal equal to a root's default value across all production assemblies,
  which works for the path-shaped tokens `.grimoire` and `llm-wiki`. `memory` is an
  ordinary English word already present as a production literal in
  `Grimoire.Hub.QueryConversations` — the conversation-context cache's source label, which
  is both a structured-log field value and a metric tag value. Banning it globally would
  either produce a false positive or force a rename of an unrelated observability
  contract. Rule M2 below therefore scopes the scan to `Grimoire.Hub.Runtime.Paths`, the
  only namespace permitted to compose paths at all. This is a narrower guarantee than the
  other two roots enjoy, and it is a deliberate trade: a namespace-scoped rule that stays
  true beats a global rule that gets suppressed the first time it cries wolf.
- Neutral: `EvalWorkspace`'s tasks directory becomes a sibling of the workspace's wiki
  root, mirroring production. Its `PageFiles()` tasks-exclusion filter becomes dead code
  and is removed. Eval fingerprints are unaffected — they hash instruction files, the
  policy, the fixture and the scenario definition, not the workspace layout.
- Neutral: ADR-020's `HubPathSettings` parity clause is unchanged in substance; the
  catalog it mirrors gains one entry.
- Neutral: `ResolvedGrimoirePaths` gains a `MemoryDir` member and its four record-path
  helpers (`TaskArtifactPathFor`, `ConversationRecordPathFor`, `FindingsReportPathFor`,
  `RemediationTaskRecordPathFor`) keep their signatures and their output shape.

## Superseded and amended decisions

| ADR | What changes |
| --- | --- |
| ADR-022 | Rule R1's cap grows from three named entries to four (`--data-dir`, `--agent-dir`, `--wiki-dir`, `--memory-dir`); it remains an exact enumeration with 1:1 `HubPathSettings` parity. The root/sub-path table gains `MemoryDir` and re-anchors `TasksDir`/`ConversationsDir`/`FindingsDir`/`RemediationTasksDir` from `WikiDir` to `MemoryDir`. The "Bad / deliberate" consequence placing that bookkeeping inside the wiki directory is reversed. Rule R2 is extended by the namespace-scoped clause M2 rather than by adding `memory` to its global literal list. **The `Grimoire:Paths` section is regrouped from a flat key list into four anchor groups plus `SecretsFile`, renaming every configuration key and environment variable** (CLI switch names are unchanged); "one options record" now means one options *graph* bound from one section at one composition point. Everything else — three-tier precedence, mandatory configuration file, no code defaults, agent-build distribution, one launch mode — is unchanged. |
| ADR-014 | Conversation Records are stored at `<MemoryDir>/conversations/<conversationId>.md`. The record format `grimoire-conversation/1`, the append-only lifecycle, the fail-closed read, the conversation-id charset rule, and the persistence-exemption status of `ConversationRecordStore` are all unchanged. (This also retires the stale "outside `wiki/`, git-ignored" and "beneath `<base>/data`" wording that ADR-022 had already left inaccurate.) |
| ADR-018 | Remediation Task Records are stored at `<MemoryDir>/remediation-tasks/<taskId>.md`. The state machine, the dispatch-precondition authorization gate, FIFO execution order, and the frontmatter-only write scope are unchanged. |
| ADR-003 | Its placement intent — harness bookkeeping outside the knowledge base — is restored after ADR-022 had reversed it. Task artifacts remain plain markdown files an operator can version-control; they simply do so under the memory root rather than the wiki root. The domain-state/operational-state split and the SQLite ownership rule are unchanged. |
| ADR-007 | The instruction document set, fail-closed loading, and SHA-256 traceability are unchanged. The Ingest, Query and Lint `system-prompt.md` documents drop their reserved-harness-folder guidance and the Ingest prompt's wiki-relative task link, because both describe a tree the agent can no longer reach. |
| ADR-020 | The `HubPathSettings` ⇔ `PathSwitchCatalog.All` parity requirement is unchanged in substance; the catalog it mirrors and the root help's "Server options" section each gain one entry. |

## Structural Enforcement (Constitution III)

Each rule ships with a Red/Green probe (deliberate violation → verified failure →
removal) before feature code, and runs in the standard PR pipeline. Per Constitution
v1.11.0, each rule below is classified as a **Boundary Rule** (a durable
dependency-direction guarantee; mandatory reflection/IL test) or a **Feature-Scoped
Invariant** (protects this feature's current surface shape; defaults to a classicist
behavioral test, unless justified otherwise — see below).

| Rule | Classification | Statement | Test |
| --- | --- | --- | --- |
| **M1** | Feature-Scoped Invariant | `PathSwitchCatalog.All` contains exactly four entries — `--data-dir`, `--agent-dir`, `--wiki-dir`, `--memory-dir` — and `HubPathSettings` declares exactly one `[CommandOption]` per entry, each with a `[Description]`. Adding a fifth path switch fails the build. Amends ADR-022 R1 in place. | `Grimoire.ArchTests/DirectorySwitchSurfaceRuleTests` (updated; probe: add a fifth catalog entry) |
| **M2** | Feature-Scoped Invariant | No type in namespace `Grimoire.Hub.Runtime.Paths` contains an IL string literal equal to `memory` — the memory root's default value exists only in `appsettings.json`. Namespace-scoped rather than assembly-wide, because `memory` is a legitimate production literal elsewhere (see the R2 consequence above); the scoping idiom is ADR-009's, already used by `RuntimePathsBoundaryRuleTests`. | `Grimoire.ArchTests/NoCodeLevelPathDefaultsRuleTests` (new namespace-scoped case; probe: add a `DefaultMemoryDirName = "memory"` constant to `GrimoirePathOptions`) |
| **M3** | Boundary Rule | No production assembly contains an IL string literal beginning with `[[tasks/`, `[[conversations/`, `[[findings/`, or `[[remediation-tasks/` — a wiki-relative link into a harness-record folder is dangling by construction once those folders anchor outside the wiki tree. Tripwire idiom, same as ADR-009's `rev-parse` scan. | `Grimoire.ArchTests/NoWikiRelativeHarnessRecordLinkRuleTests` (new; probe: restore `RestartReconciler`'s `Task: [[tasks/{taskId}.md]]` paragraph) |
| **M4** | Feature-Scoped Invariant | `GrimoirePathOptions` declares exactly four root-group properties (`Data`, `Wiki`, `Agent`, `Memory`), each of a group type declaring a `Dir` string property plus zero or more sub-path string properties, and exactly one ungrouped property (`SecretsFile`). No path-valued property may sit directly on `GrimoirePathOptions` besides `SecretsFile`. This keeps the options graph and the JSON tree the same shape, so the file cannot silently drift back toward flatness. | `Grimoire.ArchTests/PathOptionsGroupingRuleTests` (new; probe: add a loose `TasksDir` string property directly to `GrimoirePathOptions`) |
| **M5** | Feature-Scoped Invariant (already a classicist behavioral test — no further action needed) | **The grouping is the anchoring.** For each root group `G`, resolving a configuration in which only `G:Dir` is relocated moves every resolved location derived from a sub-path property of `G`, and moves no location derived from any other group. Driven by reflection over the options graph, so a newly added sub-path is covered without touching the test. Integration tier (it must run the real resolver against the real filesystem), and it subsumes the 4×4 root-independence matrix rather than sitting beside it. | `Grimoire.IntegrationTests/PathConfiguration/PathGroupingInvariantTests` (new; probe: anchor a `Memory` group sub-path at `dataDir` in `GrimoirePathResolver`) |
| ADR-022 R2 (existing) | — (governed by ADR-022, not classified here) | No production assembly contains an IL string literal equal to `.grimoire` or `llm-wiki`. | existing `NoCodeLevelPathDefaultsRuleTests`, unchanged |
| ADR-022 R3/R4 (existing) | — (governed by ADR-022, not classified here) | Instruction-authorship boundary; no runtime build invocation. | existing rules, unchanged |
| ADR-009 (existing) | — (governed by ADR-009, not classified here) | Ambient process-context reads confined to `Grimoire.Hub.Runtime.Paths`; no `rev-parse` / `--show-toplevel` literals. | existing `RuntimePathsBoundaryRuleTests`, unchanged |
| ADR-010 C1 (existing) | — (governed by ADR-010, not classified here) | Persistence/local-filesystem stores stay concrete and namespace-contained. `ConversationRecordStore`, `FindingsReportStore`, `RemediationTaskRecordStore` and `KanbanBoardProjectionStore` change only which resolved path they are handed. | existing `HexagonalPortsAdapterRuleTests`, unchanged scope |

**Classification rationale**: M1 and M4 match Constitution Principle III's own worked
examples of a Feature-Scoped Invariant verbatim ("the CLI exposes exactly N named path
switches," "the options graph mirrors the config file's grouping"); M2 matches "no
code-level literal duplicates a config default." None of the three is a durable
dependency-direction rule — each pins one feature's current surface shape, expected to
change again if a future ADR adds a fifth root. M3, by contrast, is not tied to a count or
shape that grows with the feature: it is a durable guarantee that no production code
embeds a reference into a location structurally outside the wiki tree, the same idiom and
category as the existing Boundary Rule `RuntimePathsBoundaryRuleTests` (ADR-009). M5 is a
Feature-Scoped Invariant already covered by exactly the classicist, state-based behavioral
test Principle III requires — no further action needed.

M1, M2, and M4 keep their reflection/IL Phase 0 tests under Principle III's escape valve
(a Feature-Scoped Invariant may stay reflection-enforced where this plan explicitly
justifies why no runtime-observable behavior can catch the violation before merge); that
per-rule justification is recorded in `specs/022-memory-directory-root/plan.md`'s Test
Strategy section, not repeated here.
