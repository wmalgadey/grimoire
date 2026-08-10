# Contract: Terminology Rename Map

**Feature**: 022-align-wiki-structure

"Article" is the canonical term for a unit of wiki content. "Page" is retired. Pre-1.0, so this
is a clean break: no migration, no alias period, no dual-write.

## Rename — telemetry

| Before | After | Kind |
|--------|-------|------|
| `wiki.ingest.pages_touched_total` | `wiki.ingest.articles_touched_total` | Counter (labels unchanged: `action=created\|updated\|superseded`) |
| `wiki.query.synthesis_pages_created_total` | `wiki.query.synthesis_articles_created_total` | Counter |
| `wiki.query.synthesis_page_created` | `wiki.query.synthesis_article_created` | Log event name |
| `pages_created`, `pages_updated`, `pages_superseded` | `articles_created`, `articles_updated`, `articles_superseded` | Fields on `ingest.agent.completed`; span tags on `ingest_agent.finalize_artifact` |
| "Number of wiki pages created, updated, or superseded" | "Number of wiki articles created, updated, or superseded" | Metric description |
| "Pages whose inbound-link count was updated" | "Articles whose inbound-link count was updated" | Description of `hub.lint.inbound_links_refreshed_total` (name unchanged) |

**Invariant (SC-015)**: a renamed signal counts exactly what it counted before — same trigger,
same labels, same cardinality. Verified by running an identical scripted run against the paired
fixture and comparing values.

## Rename — persisted and wire formats

| Before | After | Where |
|--------|-------|-------|
| `pages_touched`, `pages_created`, `pages_updated`, `pages_superseded` | `articles_*` | Task artifact frontmatter |
| `created_pages:` | `created_articles:` | Conversation record bookkeeping |
| `createdPages` | `createdArticles` | NDJSON terminal `completed` event |

Renaming `createdPages` also resolves an existing divergence: ADR-015 names the field
`createdArtifacts` while the shipped code has `createdPages`.

**Compatibility**: the conversation-record parser's `default:` branch tolerates unknown keys —
its comment names feature 012's `created_pages` as the reason it exists. A legacy record's old
key is ignored, not treated as structurally unreadable, so the fail-closed
`conversation_record_unreadable` path does not fire. This is the one place the clean break could
surface as a runtime failure; it carries an explicit test.

## Rename — C# identifiers

| Before | After |
|--------|-------|
| `PagesTouched`, `PagesCreated`, `PagesUpdated`, `PagesSuperseded` | `ArticlesTouched`, `ArticlesCreated`, `ArticlesUpdated`, `ArticlesSuperseded` |
| `CreatedPages`, `CreatedPagesOrEmpty` | `CreatedArticles`, `CreatedArticlesOrEmpty` |
| `RecordPagesTouched`, `_pagesTouchedTotal` | `RecordArticlesTouched`, `_articlesTouchedTotal` |
| `PageFiles`, `pageFiles`, `touchedPageFiles`, `touchedPageContents` | `ArticleFiles`, `articleFiles`, `touchedArticleFiles`, `touchedArticleContents` |
| `LooksLikeHallucinatedPageName`, `KnownFixturePages` | `LooksLikeHallucinatedArticleName`, `KnownFixtureArticles` |

Approximately 308 occurrences across `backend/src/**/*.cs`, concentrated in
`Grimoire.IngestAgent`, `Grimoire.Hub/QueryConversations`, and `Grimoire.EvalRunner/Scoring`.

## Rename — prose

- All three agent system prompts (~190 occurrences: ingest 75, lint 61, query 54), including
  the section headings `## Page Types` → `## Article Types` and `## Page Language` →
  `## Article Language`, and the Confidence Scoring row label "Page contains an explicit
  contradiction marker" — which the lint prompt cross-references verbatim, so both change
  together.
- "Synthesis Page" → "Synthesis Article" throughout the query prompt, ADR-015 and ADR-016. It is
  a defined term with no persisted representation, and it feeds two of the renamed signal names.
- Placeholder slugs in prompt examples: `[[new-page-slug]]` → `[[new-article-slug]]`,
  `[[other-page]]` → `[[other-article]]`, `[[some-page]]` → `[[some-article]]`.
- Operator-facing CLI help: the `--wiki-dir` description "wiki pages, index.md, log.md, …".
- Nine ADRs' stale current-state descriptions (see research.md R11).

**Note**: two prompt strings are written *into wiki content* — the supersession notice
`> ⚠️ This page has been superseded by …` and the contradiction notice
`> ⚠️ Contradiction with …`. Renaming these changes what agents emit into articles from now on.
Intentional under FR-019; existing articles are not rewritten (FR-012).

## Not renamed

| Kept | Reason |
|------|--------|
| Frontmatter `type:` values (`Concept`, `Technology`, `Tool`, `Person`, `Organisation`, `Event`, `Hobby`, `Personal`, `Source summary`) | Data already written into existing wiki files; FR-012 forbids rewriting content |
| `targetPath`, `inbound_links`, `superseded_by`, `supersedes`, `confidence`, `confidence_reason`, `last_reviewed`, `review_date` | Machine-read transport/frontmatter keys carrying no wiki-content meaning |
| Denial reason constants (`create_only_target_exists`, …) | Harness contract strings |
| OTel service names, `*_agent.*` span prefixes, `task_id`, `turn_id` | ADR-013 frozen identities; none contains the retired term |
| `hub.lint.inbound_links_refreshed_total` (name) | Clean already; only its description changes |
| SvelteKit `+page.svelte`, `entries/pages/`, `frontend/src/routes/**` | Framework filenames, unrelated meaning; `frontend/` is excluded from the rule's scan surface |
| SQLite tables and columns | None contains the term — verified against the DDL |
| CLI argument names | None contains the term — verified by scan |

## Relationship to earlier features' contract rows

`wiki.ingest.pages_touched_total` was declared in the 001 and 002 plans; the
`ingest.agent.completed` field names in 002; `wiki.query.synthesis_pages_created_total` and
`created_pages:` in 012. Per the Constitution's non-retroactivity clause those features are not
rendered non-compliant. Feature 022 declares the renamed signals as its own contract rows with
its own implementation, test, and CI tasks.
