# Data Model: Wiki Storage Layout & Shared Log/Catalog Format

Entities from spec.md `## Key Entities`, refined with the decisions in `research.md`.
This feature reshapes existing runtime locations and two content formats; it adds no
new persisted entity type.

## Runtime Path Locations *(config, extends the ADR-009 `GrimoirePathOptions`/`ResolvedGrimoirePaths` shape)*

| Location | Today | After this feature | Anchor |
|---|---|---|---|
| `ContentRoot` | `<base>/wiki` | Unchanged | `baseDir` |
| ~~`PagesDir`~~ | `<content-root>/pages` | **Removed** — callers use `ContentRoot` directly (R1) | — |
| `TasksDir` | `<content-root>/tasks` (hardcoded, no option field) | `<base>/tasks` — **new** `GrimoirePathOptions.TasksDir` field + `DefaultTasksDirName = "tasks"`, own `BuildLocation`/validation entry | `baseDir` (R2) |
| `ConversationsDir` | `<data-dir>/conversations` | `<base>/conversations` — same option field/default name, anchor changes | `baseDir` (R2) |
| `IndexPath` | `<content-root>/index.md` | Unchanged | `contentRoot` |
| `LogPath` | `<content-root>/log.md` | Unchanged | `contentRoot` |
| `DataDir` and everything under it (`RawDir`, `StateDb`, `SecretsFile`, `InstructionsDir`/`QueryInstructionsDir`/`LintInstructionsDir`, `WriteLocksDir`, `FindingsDir`) | `<base>/data/...` | Unchanged (FR-005) | `baseDir` |

Internal Hub↔agent-process CLI contract: `--pages-dir` renamed to `--content-root`
(`AgentProcessHost.cs`, `IngestCliOptions.cs`, `QueryCliOptions.cs`, `LintCliOptions.cs`).

## Write Rule *(config, extends the ADR-015/ADR-016 `WriteRule` shape — no new mode)*

No new `mode` value. What changes is `pathPrefix` values in `data/agents/*/policy.json`
(R3, R4):

`data/agents/ingest/policy.json` (was: `pages/`, `tasks/`, `index.md`, `log.md`):

```json
{
  "version": 2,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" },
    { "pathPrefix": "." }
  ],
  "write": [
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" },
    { "pathPrefix": "." }
  ]
}
```

`data/agents/query/policy.json` (was: `pages/` create-only, `index.md`, `log.md`):

```json
{
  "version": 2,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" },
    { "pathPrefix": "." }
  ],
  "write": [
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" },
    { "pathPrefix": ".", "mode": "create-only" }
  ]
}
```

`data/agents/lint/policy.json` (was: `pages/` frontmatter-only; no index/log write):

```json
{
  "version": 1,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" },
    { "pathPrefix": "." }
  ],
  "write": [
    { "pathPrefix": ".", "mode": "frontmatter-only" }
  ]
}
```

`PolicyLoader.NormalizeRulePrefix` gains one new case: the literal `"."` resolves to
the policy's `_wikiRoot` anchor (`ContentRoot`) itself, treated directory-style —
matching the anchor and everything under it, the same way a trailing-slash prefix does
today. Rule order matters (first-match-wins, no deny/exclude rule type exists): the
exact-match `index.md`/`log.md` entries must precede `.` in both arrays so those two
files keep their own (unrestricted, or absent for Lint) rule instead of falling
through to `.`'s mode.

## Denied Action *(extends the existing `DeniedActionRecord`, no shape change — ADR-017)*

Four new `reason` values, alongside ADR-015's/ADR-016's existing ones:

| Reason | Meaning |
|---|---|
| `log_entry_not_appended` | Write denied: proposed `log.md` content does not extend the current content byte-for-byte (append-only violation, FR-011) |
| `log_entry_malformed_heading` | Write denied: the appended tail's first non-blank line does not match `[DATE] TYPE \| SUMMARY` |
| `log_entry_missing_paragraph` | Write denied: a heading was appended with no following non-blank paragraph line |
| `catalog_entry_malformed` | Write denied: a brand-new `- [`-led line in the proposed `index.md` content does not match the link—description—status shape |

## Log Entry *(content format, `log.md`, append-only)*

| Field | Shape | Notes |
|---|---|---|
| Heading | `## [YYYY-MM-DD] TYPE \| SUMMARY` | `##` level (spec Assumptions); `DATE` is ISO calendar date, no time (FR-007); `TYPE` is open-ended, agent-chosen (e.g. `ingest`, `update`, `query`, `lint-fixes` — spec Assumptions); `SUMMARY` is a short agent-authored phrase, not a re-encoded field list |
| Body | One short prose paragraph, immediately following the heading | Describes what was actually done (FR-008); evaluated for specificity, not genericness, at SC-005's ≥90% threshold |
| Locatability | Heading independently locatable by pattern search (e.g. `^## \[\d{4}-\d{2}-\d{2}\] `) | FR-011, SC-004 |

Applies identically regardless of author — agent-written (FR-009) or the
`WikiLogAppender` backstop (FR-010, R5). The backstop's own heading/paragraph is
generated harness content, not agent narrative, so it is exempt from the SC-005
agent-judgment quality threshold but still structurally conforms to FR-007/FR-008.

## Catalog Entry *(content format, `index.md`)*

| Field | Shape | Notes |
|---|---|---|
| Link | Markdown link to the article | Relative to content root |
| Description | Short prose, in the wiki's configured content language | German by default, or the operator's configured language (spec Clarifications) — never CLAUDE.md's English-only policy, which governs this repo's own code/docs, not agent-generated wiki content |
| Source-status marker | A source count, or a stub indicator | Agent's own judgment (spec Assumptions) — not a new structured/tracked field |

Instruction-file format only (R6) — no backend entity, no persisted schema beyond the
markdown line itself.

## Relationships

- `TasksDir` and `ConversationsDir` are siblings of `ContentRoot`, both anchored at
  `baseDir` — neither nested inside the other, neither nested inside `ContentRoot` or
  `DataDir` (FR-003, FR-004).
- A Log Entry's `TYPE` is free text chosen by the appending agent or the backstop; it
  is not a foreign key into any other entity.
- A Catalog Entry's link targets exactly one Article under a topical subfolder of
  `ContentRoot`; the topical subfolder name is not a tracked entity, only a directory
  agents create ad hoc (spec Assumptions).

## Retired / superseded entities

- `PagesDir` / `ResolvedGrimoirePaths.PagesDir` (retired — R1).
- The bulleted `* **Verb**: <text>` log-entry sub-format documented in
  `data/agents/ingest/system-prompt.md` (retired — R5, replaced by the heading +
  paragraph shape).
- `IngestLogAppender`'s own `## [{date}] ingest | ... | task: [[...]]` heading shape
  (retired — R5, replaced by `WikiLogAppender`'s `[DATE] TYPE | SUMMARY`).
