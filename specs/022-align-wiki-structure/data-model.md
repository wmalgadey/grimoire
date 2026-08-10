# Phase 1 Data Model: Wiki Structure Truth

**Feature**: 022-align-wiki-structure | **Date**: 2026-08-10

Two model areas: the **wiki content root's composition** (what an agent sees and how it is
named), and the **harness-surface read scope** (a new guardrail input and its provenance).

---

## 1. Wiki content root

### Wiki Content Root

The single directory the operator points the harness at (`Grimoire:Paths:WikiDir`). It holds
the wiki's own parts and the reserved harness surfaces side by side.

| Member | Cardinality | Notes |
|--------|-------------|-------|
| `index.md` | 0..1 | The Catalog. Absent on a fresh root; created by an agent on first write. |
| `log.md` | 0..1 | The Activity Log. Same lifecycle. |
| Category Folder | 0..n | Open-ended. Any top-level directory whose name is not a reserved harness surface. |
| Harness Surface | 0..4 | Reserved names, harness-owned. |

**Invariant**: no wrapper directory sits between the content root and a category folder. An
article's path is `<category>/<slug>.md`, exactly two segments relative to the root (deeper
nesting is permitted where a category has sub-topics, but the first segment is always the
category).

**Invariant**: the reserved names win at the top level. An agent may not create a category
folder named `tasks`, `conversations`, `findings`, or `remediation-tasks`.

### Catalog (`index.md`)

| Field | Rule |
|-------|------|
| Entry line | `- [Title](content-root-relative/path.md) — description — status` |
| Structural constraint | Every newly added `- [`-led line must match `^- \[.+\]\(.+\) — .+ — .+$` or the write is denied `catalog_entry_malformed` (ADR-017) |
| Link form | **Markdown link**, not a wikilink — the guard's regex cannot be satisfied by `[[slug]]` |
| Lifecycle | Created by an agent on first write when absent |

### Activity Log (`log.md`)

| Field | Rule |
|-------|------|
| Entry heading | `## [YYYY-MM-DD] TYPE \| SUMMARY`, matching `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` |
| Body | At least one non-blank line after the heading |
| Append-only | Proposed content must start with current on-disk content byte-for-byte, **or the file must not yet exist** (ADR-017 — this clause is what makes create-on-first-write already legal) |
| `TYPE` values | `ingest`, `query`, `lint` — the writing agent's kind |

### Category Folder

| Attribute | Value |
|-----------|-------|
| Name | Agent-chosen, lowercase slug; illustrative set `tech`, `tools`, `concepts`, `events`, `people`, `organisations`, `hobbies`, `personal`, `sources` |
| Openness | Open-ended — an agent creates a new one when none fits |
| Constraint | Must not collide with a reserved harness surface name |

### Article

The unit of wiki content, and the project's canonical term for it. "Page" is retired.

| Attribute | Value |
|-----------|-------|
| Path | `<category>/<slug>.md` relative to the content root |
| Frontmatter | Opens with exactly two `---` delimiter lines; carries `type`, `title`, `description`, `timestamp`, `tags`, `confidence`, `confidence_reason`, optionally `superseded_by`, `supersedes`, `inbound_links`, `last_reviewed`, `review_date` |
| `type` values | `Concept`, `Technology`, `Tool`, `Person`, `Organisation`, `Event`, `Hobby`, `Personal`, `Source summary` — **unchanged**, these are data in existing wiki files |
| Reference form | Wikilink `[[slug]]` or `[[category/slug]]` in prose and frontmatter; markdown link in the Catalog only |

**Wikilink resolution**: by filename, folder-agnostic. Only the final path segment is
significant — `[[slug]]` and `[[category/slug]]` name the same article. Resolved by the agent
against the filenames it enumerated, never by constructing a path.

**Frontmatter invariant (load-bearing)**: ADR-016's frontmatter-only write mode splits document
from body lexically at the first two `---` lines, justified explicitly by the ingest prompt's
requirement that every article opens with that shape. Loosening the convention breaks Lint's
writes with `frontmatter_only_malformed_document`.

### Harness Surface

A reserved top-level directory the harness owns. Records what agents did and enables operator
interaction. **Not wiki content**: never an article category, never a write target for an
agent, never a citable source for a wiki answer, never derivable into an article.

| Name | Written by | Contains |
|------|-----------|----------|
| `tasks` | Ingest agent, Hub | Task artifacts |
| `conversations` | Hub | Query conversation records (`grimoire-conversation/1`) |
| `findings` | Lint agent, Hub | Findings reports |
| `remediation-tasks` | Hub | Remediation task records |

The set is closed and declared in exactly one place (`ReservedHarnessSurfaces`); the
denied-subtree derivation reads it from there rather than repeating literals (ADR-023 rule H2).

---

## 2. Harness-surface read scope

### HarnessSurfaceReadOptions

Configuration record bound from `Grimoire:HarnessSurfaceReads`.

| Property | Type | Default | Meaning |
|----------|------|---------|---------|
| `Tasks` | bool | `false` | Agents may read `tasks/` |
| `Conversations` | bool | `false` | Agents may read `conversations/` |
| `Findings` | bool | `false` | Agents may read `findings/` |
| `RemediationTasks` | bool | `false` | Agents may read `remediation-tasks/` |

- Bound in `HubHostComposition`, following `LintReviewWindowOptions`.
- All four keys written explicitly into `appsettings.json` as `false`, so the posture is visible
  rather than implied by absence.
- Environment override works out of the box: `Grimoire__HarnessSurfaceReads__Tasks=true`.
- **No CLI switch** — ADR-022's cap is on path switches, but this setting takes none regardless.
- The grant applies uniformly to every agent. No per-agent variant exists.

### Effective grant set (per run)

Derived once, at spawn, by the Hub; carried to the agent as a single CLI argument; recorded on
the run.

| Field | Type | Notes |
|-------|------|-------|
| `granted_surfaces` | ordered list of surface names | Empty on a default installation |
| `denied_surfaces` | ordered list of surface names | Complement of granted within the reserved set |

### SafetyPolicy extension

| Element | Before | After |
|---------|--------|-------|
| Read state | `IReadOnlyList<string> _readPrefixes` | plus `IReadOnlyList<string> _deniedReadSubtrees` |
| Read evaluation | allow loop → `no_rule` | **denied-subtree check first** → `harness_surface_not_granted`; then allow loop → `no_rule` |
| Matching | — | directory-style, matching the subtree **and the bare directory itself**, so `list_files("tasks")` is denied |
| Purity | dependency-free, no I/O | unchanged — takes plain strings; the boolean→subtree mapping lives in agent composition |
| Loaded policy identity | path + version + sha256 | **unchanged** — `policy.json` is not modified by this feature |

Modelled on the existing `WithNoWriteAccess()`, whose documentation already draws the
distinction: the loaded identity describes what was read from disk; a runtime narrowing changes
only what the in-memory instance enforces for that run.

### Denial reason vocabulary

Existing reasons are bare string literals with no enum: `traversal`, `no_rule`, `out_of_scope`,
`write_coordination_timeout`, `create_only_target_exists`, `frontmatter_only_target_missing`,
`write_conflict_stale_read`, `frontmatter_only_malformed_document`,
`frontmatter_only_body_changed`, `log_entry_not_appended`, `log_entry_malformed_heading`,
`log_entry_missing_paragraph`, `catalog_entry_malformed`.

**Added**: `harness_surface_not_granted`.

Distinct from `no_rule` because SC-010 requires the operator to distinguish "you have not
granted this" from "this is outside the policy", and the reason is echoed to the agent in the
tool result. Documented on `DeniedActionRecord`'s XML comment, which is the canonical home of
the vocabulary.

`DeniedActionRecord(Action, RequestedTarget, CanonicalTarget, Reason, Turn)` is unchanged in
shape — the new reason flows through the existing `RecordDenial` funnel, which already emits
instrumentation and returns an `is_error` tool result instructing the agent to continue.

---

## 3. Renamed persisted and wire fields

No SQLite table or column is affected. No CLI argument name is affected.

| Location | Before | After | On disk / wire? |
|----------|--------|-------|-----------------|
| Task artifact frontmatter | `pages_touched`, `pages_created`, `pages_updated`, `pages_superseded` | `articles_touched`, `articles_created`, `articles_updated`, `articles_superseded` | **Yes** |
| Conversation record bookkeeping | `created_pages:` | `created_articles:` | **Yes** |
| NDJSON terminal event | `createdPages` | `createdArticles` | **Yes** (also resolves ADR-015's existing name divergence) |
| Task artifact document | `PagesTouched`, `PagesCreated`, `PagesUpdated`, `PagesSuperseded` | `Articles*` | C# only |
| Recorded turn / turn state | `CreatedPages`, `CreatedPagesOrEmpty` | `CreatedArticles`, `CreatedArticlesOrEmpty` | C# only |
| Metrics | `wiki.ingest.pages_touched_total`, `wiki.query.synthesis_pages_created_total` | `wiki.ingest.articles_touched_total`, `wiki.query.synthesis_articles_created_total` | **Yes** (telemetry) |
| Log event fields | `pages_created`, `pages_updated`, `pages_superseded` on `ingest.agent.completed` | `articles_*` | **Yes** (telemetry) |
| Log event name | `wiki.query.synthesis_page_created` | `wiki.query.synthesis_article_created` | **Yes** (telemetry) |
| Metric description | "Pages whose inbound-link count was updated" | "Articles whose…" | Operator-visible text |
| CLI help text | `--wiki-dir` description "wiki pages, index.md, …" | "wiki articles, index.md, …" | Operator-visible text |

**New fields**, added alongside the existing `policy:` provenance block in each record:
`granted_harness_surfaces` (task artifact frontmatter, terminal event, conversation record
bookkeeping).

### Compatibility note

The conversation-record parser's `default:` branch tolerates unknown keys explicitly — its
comment names feature 012's `created_pages` as the reason. A legacy record carrying the old key
is therefore ignored, not treated as structurally unreadable, so the fail-closed
`conversation_record_unreadable` path does not fire. This is the one place where the clean break
could otherwise surface as a runtime failure and it must carry an explicit test.

**Not renamed**: frontmatter `type:` values; machine-read transport keys `targetPath`,
`inbound_links`, `superseded_by`, `supersedes`, `confidence`, `confidence_reason`,
`last_reviewed`; denial reason constants; OTel service names, `*_agent.*` span prefixes,
`task_id`/`turn_id` (ADR-013 frozen identities, none of which contain the retired term);
SvelteKit `+page.svelte` framework filenames.
