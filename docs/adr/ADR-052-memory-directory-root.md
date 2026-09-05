---
status: accepted
supersedes: ADR-024
---

# ADR-052: Memory Directory — A Fourth Independent Root for Agent Process Bookkeeping

## Context and Problem Statement

Before this decision, four bookkeeping locations — task artifacts, conversation records,
findings reports, and remediation-task records — lived anchored under the wiki root, mixing the
maintained knowledge base (the product) with harness record-keeping of what the agents did
(process exhaust). An operator who wanted to back up, retain, or exclude one but not the other had
no single location to point at.

ADR-024 fixed this by adding a fourth independent, cwd-anchored root (`MemoryDir`) and re-anchoring
the four bookkeeping locations beneath it, alongside regrouping the configuration file by anchoring
root and fixing a dangling wiki-relative task link. ADR-032 subsequently reversed part of what
ADR-024 decided: ADR-024's Structural Enforcement section chose reflection/IL Phase 0 tests for
rules M1, M2, and M4, justified by an "escape valve" the constitution does not contain; ADR-032
retracted that justification and replaced the enforcement mechanism with classicist behavioral
tests, while leaving the rules' substance (what M1/M2/M4 actually require) untouched. A reversal of
part of an Accepted ADR's decided content — even when confined to one section and even when the
substance it enforces survives — is exactly what Constitution v2.0.0's whole-ADR supersession rule
requires ADR-024 to transition on, rather than remain "Accepted" with a live correction attached.
This ADR restates ADR-024's entire still-current decision — the four-root model, the sub-path
moves, the wikilink fix, the configuration-file grouping, and rules M1–M5 — as one current-truth
ADR, with M1/M2/M4 described under their current (behavioral) enforcement from the start.

## Decision Drivers

- An operator relocates bookkeeping for reasons independent of relocating the wiki, runtime data,
  or the agent runtime — no root's relocation may drag another along (the same independence
  contract ADR-041 established for the other three roots, extended here).
- Per-option precedence CLI > environment > configuration file, with the configuration file as the
  sole default source and a named startup failure when a key is absent (ADR-042); no code-level
  fallback.
- The four bookkeeping locations move root, and *only* root — their internal file naming and
  per-record layout are untouched.
- No automatic migration, consistent with the pre-1.0 breaking-change precedent ADR-041/ADR-042
  already set for their own layout changes.
- Instruction files must stop describing these folders as reachable within the wiki tree, because
  they no longer are.
- The anti-regrowth intent of the switch cap (ADR-041) must survive: a fourth switch is a
  deliberate, reviewed addition, not silent creep.
- Constitution Principle III: a Feature-Scoped Invariant is proven by a classicist, state-based
  behavioral test against real observable behavior — never a reflection/IL structural test, with
  no documented exception.
- Configuration-file shape must mirror the anchoring graph, so the file's diff *is* the semantics
  of a re-anchoring change, not an invisible comment edit.

## Considered Options

Restated from ADR-024 (unchanged by this ADR):

### Root shape
- **Chosen: a fourth cwd-anchored root, `MemoryDir`, with its own `--memory-dir` switch.**
- Rejected: a fourth root configurable only through `appsettings.json` (inverts which root an
  operator can actually redirect); keeping the four sub-paths anchored at `WikiDir` (the status quo
  the spec rejects); nesting the memory directory under `DataDir` (reintroduces shared-parent
  coupling).

### Which sub-paths move
- **Chosen: all four** (`TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir`)
  re-anchor at `MemoryDir`.
- Rejected: leaving `TasksDir` under the wiki as an exception — it is the clearest instance of
  process bookkeeping in the system, and an exception would keep the wiki tree mixed for no gain.

### The wiki-relative task link
- **Chosen: replace the wikilink with a bare task-id reference** (`Task: <task_id>`).
- Rejected: a wikilink pointing outside the wiki root (meaningless to a wiki reader, unresolvable
  by the guarded tool surface); dropping the task reference entirely (breaks the harness's own
  log-deduplication backstop).

### Configuration-file shape
- **Chosen: group by anchoring root** — four groups (`Data`, `Wiki`, `Agent`, `Memory`) each
  carrying a `Dir` key plus sibling sub-path keys; `SecretsFile` stays ungrouped (working-directory
  anchored, per ADR-041).
- Rejected: a flat list relying on comments (the failure mode this feature demonstrates); an
  anchor-as-data shape (invites an anchor the resolver doesn't implement); prefixed-but-flat keys
  (encodes hierarchy in naming nothing enforces).

### Superseded configuration key detection
- **Chosen: accept the silent-fallback failure mode (pre-1.0, no legacy-key detector).**
- Rejected: detecting every superseded flat configuration key at startup — permanent compatibility
  ballast for a scenario the project's pre-1.0 maturity rules out.

## Decision Outcome

**Chosen: fourth root + all four sub-paths move + bare task-id reference + anchor-grouped
configuration + no superseded-key detection**, restated as current truth.

### Four roots

`GrimoirePathOptions` carries, alongside the roots ADR-041 owns:

| Tier | Fields | Anchor | CLI switch |
| --- | --- | --- | --- |
| **Root** | `MemoryDir` | process working directory | `--memory-dir` |
| **Sub-paths** | `TasksDir`, `ConversationsDir`, `FindingsDir`, `RemediationTasksDir` | `MemoryDir` | *none* |

- `MemoryDir` is anchored at the process working directory, never beneath another root. Its shipped
  default is `memory`. Relocating any other root leaves it where it is, and relocating it leaves
  the others where they are.
- The four bookkeeping sub-paths keep their configured values (`tasks`, `conversations`, `findings`,
  `remediation-tasks`) and their internal per-record layout verbatim. Only their anchor changed
  from `WikiDir` (as ADR-024 inherited it) to `MemoryDir`.
- `MemoryDir` is a `WritableData` location: auto-created when absent, like `DataDir` and `WikiDir`
  and unlike `AgentDir`.
- An operator who explicitly configures `MemoryDir` to equal or nest inside another root gets
  exactly that; the resolver anchors and validates, it does not police a deliberate choice.

### The configuration file is grouped by anchoring root

`Grimoire:Paths` is four groups plus one ungrouped key. Each group's own root is the key `Dir`;
every other key in the group is a sub-path anchored at that group's resolved `Dir`:

```jsonc
"Grimoire": {
  "Paths": {
    "Data":   { "Dir": ".grimoire",      "RawDir": "raw",
                "StateDb": "state/operational-state.db", "WriteLocksDir": "write-locks" },
    "Wiki":   { "Dir": "llm-wiki" },
    "Agent":  { "Dir": ".grimoire/agents" },
    "Memory": { "Dir": "memory",         "TasksDir": "tasks",
                "ConversationsDir": "conversations", "FindingsDir": "findings",
                "RemediationTasksDir": "remediation-tasks" },
    "SecretsFile": ".env"
  }
}
```

Key names, switch mappings, and environment variables follow this grouping (e.g. memory root:
`Grimoire:Paths:Memory:Dir` / `Grimoire__Paths__Memory__Dir` / `--memory-dir`; task artifacts:
`Grimoire:Paths:Memory:TasksDir`). `GrimoirePathOptions` mirrors the tree: group properties of
dedicated option types, plus the ungrouped `SecretsFile` — one options record bound from one
section at one composition point (ADR-040), that place being a graph rather than a flat list.

### Superseded configuration keys are not detected

An unrecognized configuration key — file-based or environment-variable-based — is silently ignored
by the configuration binder, exactly as any other unrecognized key would be. No dedicated
legacy-key detector or log/metric event exists for this case; consistent with the project's pre-1.0
posture.

### The task reference in `log.md` is a bare id, not a wikilink

The Ingest system prompt's task reference and `RestartReconciler`'s crash-recovery paragraph both
use a bare `Task: <task_id>` form. The harness backstop (`WikiLogAppender`, matching the correlation
id as an ordinal substring) is satisfied by the bare id exactly as it was by the wikilink.

### The harness record folders are outside the agent's reachable tree

Because the four bookkeeping folders anchor at `MemoryDir` rather than under the wiki root, they
are outside the guarded root entirely: `GuardedToolExecutor` cannot list or write them, and no
policy `"."` rule covers them. Instruction-file guidance describing them as part of the wiki tree
was removed rather than reworded — it describes a tree the agent can no longer see. The agent's own
task-artifact write (`TaskArtifactStore`, direct file I/O on the path supplied by `--tasks-dir`) is
unaffected — it never passed through the guarded tool boundary.

### Rules M1–M5, with current enforcement

Each rule's substance is exactly what ADR-024 decided. Enforcement for M1, M2, and M4 is the
classicist, behavioral mechanism [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md)
established — the reflection/IL tests ADR-024 originally named for these three no longer exist.

| Rule | Classification | Statement | Current enforcement |
| --- | --- | --- | --- |
| **M1** | Feature-Scoped Invariant | Exactly four path switches — `--data-dir`, `--agent-dir`, `--wiki-dir`, `--memory-dir` — each with a description. | Out-of-process: `HubHelpUsageTests` spawns the built `Grimoire.Hub.dll`, runs `--help`, and asserts the exact `--*-dir` switch set and non-empty descriptions. |
| **M2** | Feature-Scoped Invariant | The memory root's default value (`memory`) exists only in `appsettings.json`, never as a code-level literal. | Behavioral: `StartupValidationTests` — omitting `Grimoire:Paths:Memory:Dir` throws `ConfigurationMissing` naming the full key path; a code-level fallback would make resolution silently succeed and the test fail. |
| **M3** | Boundary Rule | No production assembly contains an IL string literal beginning with `[[tasks/`, `[[conversations/`, `[[findings/`, or `[[remediation-tasks/` — a wiki-relative link into a harness-record folder is dangling by construction. | `Grimoire.ArchTests/NoWikiRelativeHarnessRecordLinkRuleTests` (IL tripwire scan, Red/Green probed) — unchanged by ADR-032, which did not touch M3. |
| **M4** | Feature-Scoped Invariant | `GrimoirePathOptions` declares exactly four root-group properties, each a group type with a `Dir` property plus zero or more sub-paths, and exactly one ungrouped property (`SecretsFile`). | Behavioral: `PathPrecedenceTests` (each root binds through its nested form independently), `PathGroupingInvariantTests`, `StartupValidationTests` (group-shape binding). |
| **M5** | Feature-Scoped Invariant | The grouping is the anchoring: relocating only a group's `Dir` moves every location derived from that group's sub-paths, and no location derived from any other group. | `Grimoire.IntegrationTests/PathConfiguration/PathGroupingInvariantTests` — unchanged by ADR-032, already classicist behavioral. |

No rule above is enforced by reflection or IL inspection of the options graph's shape or a
Feature-Scoped Invariant's cardinality; only M3's Boundary Rule (a durable no-dangling-link
guarantee, not a feature-surface cardinality) uses an IL-based tripwire, consistent with
Constitution Principle III.

### Consequences

- Good: an operator has one location to name in a backup job, a retention policy, or an exclusion
  rule — the wiki directory holds only wiki content.
- Good: harness bookkeeping is neither hidden inside git-ignored operational state nor mixed into
  the wiki — it is its own operator-visible root, restoring the intent behind the domain/operational
  state split.
- Good: the "these are not pages" contract is structural (outside the guarded tool surface), not
  solely enforced by prompt text.
- Good: every Feature-Scoped Invariant this ADR carries forward is now proven by a classicist,
  state-based test against real observable behavior, with no reflection/IL enforcement for feature
  surface shape — Principle III's rule applies with no live exception.
- Bad: the configuration-key regrouping was a breaking change with no migration; an operator with
  existing environment-variable overrides in the old flat key form gets the ordinary silent-ignore
  treatment. Accepted on the same pre-1.0 posture ADR-041/042 already applied to their own layout
  changes.
- Neutral: `EvalWorkspace`'s tasks-directory handling mirrors production; eval fingerprints are
  unaffected (they hash instruction files, policy, fixture, and scenario definition, not workspace
  layout).

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new sub-path added beneath `MemoryDir` in the
  configuration file; a new consumer reading a memory-anchored location; growing M1's switch
  enumeration by one root through the same pattern (a new ADR naming the root, per ADR-041's own
  Change Triggers) without touching this ADR's own four-switch table in place — that table's
  correctness after such a change is exactly what would make *this* ADR a candidate for the same
  whole-ADR treatment ADR-024 received, not a silent edit.
- **Invalidations (would require full supersession):** re-anchoring any of the four bookkeeping
  sub-paths away from `MemoryDir`; reintroducing a reflection/IL structural test to enforce M1, M2,
  or M4; collapsing `MemoryDir` back into another root; reintroducing superseded-configuration-key
  detection as a decision (rather than as a one-off compatibility patch outside this ADR's scope).

## More Information

Supersedes [ADR-024](ADR-024-memory-directory-root.md), folding in the enforcement-mechanism
correction [ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md) recorded
against it.

Read alongside: [ADR-040](ADR-040-runtime-path-composition.md) — the single composition point and
precedence chain this root resolves through; [ADR-041](ADR-041-independent-directory-roots.md) —
the other three roots and the switch-cap pattern this ADR extends; [ADR-042](ADR-042-mandatory-configuration-file.md)
— the mandatory configuration file this ADR's grouping shape lives inside;
[ADR-032](ADR-032-behavioral-enforcement-for-path-surface-invariants.md) — the behavioral
enforcement style M1/M2/M4 follow; [ADR-014](ADR-014-query-conversation-records.md) — Conversation
Records, now stored at `<MemoryDir>/conversations/<conversationId>.md`; [ADR-018](ADR-018-remediation-action-authorization-and-execution.md)
— Remediation Task Records, now stored at `<MemoryDir>/remediation-tasks/<taskId>.md`;
[ADR-003](ADR-003-domain-operational-state-persistence.md) — the domain-state/operational-state
split this root's placement restores the intent of; [ADR-007](ADR-007-agent-instruction-surface.md)
— the instruction documents whose reserved-harness-folder guidance was removed as part of this
decision. None of their decisions are restated or narrowed here.
