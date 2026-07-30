# Data Model: Lint Agent — Wiki Health Check

Entities from spec.md `## Key Entities`, refined with the decisions in `research.md`
and ADR-016. Lint reuses ADR-015's Write Rule / Write-Coordination Lock / Denied
Action shapes from `specs/012-query-synthesis-writes/data-model.md` unchanged except
for the extension below.

## Write Rule *(config, extends the ADR-015 shape)*

`WriteRule`'s single boolean (`create-only` vs. default) becomes a three-way mode.
Backward compatible: existing `read-write`/`create-only` policy files (Ingest, Query)
need no edits — `frontmatter-only` is a new, additive value.

| Field | Type | Notes |
|---|---|---|
| `pathPrefix` | string | Unchanged existing field |
| `mode` | `read-write` \| `create-only` \| `frontmatter-only` | New value: `frontmatter-only`. Denied if the target doesn't exist (`frontmatter_only_target_missing`); denied if the current-vs-proposed content's bodies differ (`frontmatter_only_body_changed`) or either isn't a well-formed two-delimiter frontmatter document (`frontmatter_only_malformed_document`); otherwise subject to the same compare-and-swap check as `read-write` |

`data/agents/lint/policy.json`:

```json
{
  "version": 1,
  "defaultDecision": "deny",
  "read": [
    { "pathPrefix": "pages/" },
    { "pathPrefix": "index.md" },
    { "pathPrefix": "log.md" }
  ],
  "write": [
    { "pathPrefix": "pages/", "mode": "frontmatter-only" }
  ]
}
```

Note: no write rule for `index.md`/`log.md` — Lint does not maintain the index or log
(only Ingest and Query create pages; Lint only refreshes metadata on existing pages).

## Denied Action *(extends the existing `DeniedActionRecord`, no shape change)*

Three new `reason` values, alongside ADR-015's existing ones:

| Reason | Meaning |
|---|---|
| `frontmatter_only_target_missing` | Write denied: `frontmatter-only` rule, but the target does not exist |
| `frontmatter_only_malformed_document` | Write denied: current or proposed content is not a well-formed two-delimiter frontmatter document |
| `frontmatter_only_body_changed` | Write denied: the content after the closing `---` differs between current and proposed |

## Lint Run *(operational record, Hub-managed)*

One execution of the Lint agent.

| Field | Type | Notes |
|---|---|---|
| `run_id` | string | Hub-generated, e.g. `<date>-lint-<random>` mirroring existing `task_id`/`turn_id` shapes |
| `trigger_time` | timestamp | When the run was accepted |
| `instruction_file` | `{ path, sha256 }` | Lint System Prompt identity, fail-closed load |
| `outcome_state` | `completed` \| `failed` | Terminal only |
| `failure_reason` | string \| null | `outcome_state = failed` only |
| `denied_actions` | list of `{ action, requested_target, canonical_target, reason, turn }` | Same shape as every other agent's denials |

Not itself a durable file — the run's terminal facts are folded into the Findings
Report's own frontmatter/bookkeeping (below), so there is exactly one artifact per
run, not two.

## Findings Report *(file, Hub-written, one per Lint Run)*

Stored at `<base>/data/findings/<runId>.md`
(`ResolvedGrimoirePaths.FindingsReportPathFor`, ADR-009 pattern; outside `wiki/`,
git-ignored per ADR-003). Written once, at the run's terminal transition — never
appended to (unlike the Conversation Record, a Findings Report has exactly one
"turn": the run itself). Format: `contracts/findings-report-format.md`.

### Run-level facts (frontmatter, written once)

| Field | Type | Notes |
|---|---|---|
| `run_id` | string | Matches the Lint Run's identity |
| `triggered_at` / `completed_at` | timestamp | |
| `outcome_state` | `completed` \| `failed` | |
| `failure_reason` | string \| null | |
| `instruction_file` | `{ path, sha256 }` | |
| `denied_actions` | list | Same shape as Query/Ingest |
| `inbound_links_refreshed` | int | Count of pages whose frontmatter link-count write succeeded |
| `partial` | bool | `true` if the run did not reach a clean terminal state (e.g. liveness failure mid-analysis) — the report is still persisted and clearly marked, never silently truncated (spec edge case) |

### Findings (body, one section per category)

| Field | Type | Notes |
|---|---|---|
| `category` | `content_quality` \| `metadata_hygiene` \| `structure` | Spec's three Finding Categories |
| `affected_pages` | list of wikilinks | |
| `description` | string | Agent-authored |
| `proposed_remediation` | string | Agent-authored |

An empty findings list (healthy wiki) is a valid, honest report — rendered as an
explicit "no findings" statement per category, never omitted or fabricated (spec
edge case, FR-006 acceptance scenario 4).

## Relationships

- 1 Lint Run : 1 Findings Report (never many; never zero — a failed run still gets a
  report, marked `partial` if analysis didn't complete).
- Findings Report references wiki pages by wikilink only (read-only references); it
  does not modify the pages it discusses beyond the separate, mechanical Inbound-Link
  Refresh write.
- Review Candidates (spec's Key Entity) are not a separate stored entity — they are
  metadata-hygiene findings whose selection rule is the Review Window (default 90
  days) applied to a low-confidence page's `last_reviewed` date, computed and reported
  by the agent under its instruction file, not a backend rule.

## State transitions

```text
POST /api/lint-runs
  → LintRunCoordinator.TryStartAsync()
      semaphore already held (a run is active)
          → 409/429 "busy" (SC-003, no queue, no persisted state)
      semaphore acquired
          → spawn Grimoire.LintAgent process
              → whole-wiki read (list_files/read_file, unbounded — ADR-006 tools)
              → zero or more frontmatter-only write_file calls
                  (each: SafetyPolicy → SharedFileWriteGuard: exists? → CAS → body-diff)
              → final narrative (findings, categorized)
          → terminal event (completed | failed)
              → FindingsReportStore.WriteAsync(runId, narrative, bookkeeping)
              → semaphore released
```

## Retired / superseded entities

None. This feature adds a new agent and its supporting types; it does not retire or
change any existing entity from features 002–012.
