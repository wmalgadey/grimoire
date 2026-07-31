# Contract: `log.md` Entry Format and `index.md` Catalog Entry Format

The two content formats FR-007–FR-013 standardize across every agent type (ingest,
query, lint, and the harness backstop). Both are markdown line/block shapes inside
existing files — not a new file, not a new persisted schema.

## `log.md` Entry

Append-only. `## `-level heading, immediately followed (no blank content in between
other than the required blank line after any markdown heading) by one prose
paragraph.

```markdown
## [YYYY-MM-DD] TYPE | SUMMARY

<One short prose paragraph describing what was actually done — source references,
task/conversation links, and outcome detail belong here, not in the heading.>
```

- `YYYY-MM-DD`: ISO 8601 calendar date, no time component (FR-007).
- `TYPE`: free text, agent- or backstop-chosen (e.g. `ingest`, `update`, `query`,
  `lint-fixes`) — not a fixed enum (spec Assumptions).
- `SUMMARY`: a short phrase, not a re-encoded field list.
- The heading MUST be independently locatable by searching for the pattern
  `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$` (FR-011, SC-004).
- Identical shape regardless of author: agent-written entry or the `WikiLogAppender`
  harness backstop (FR-009, FR-010). The backstop's heading/paragraph is
  harness-generated factual text (what ran, what it touched, why the backstop fired),
  never fabricated narrative.
- Two entries may share an identical heading (same date, type, summary) for two
  distinct actions — the format does not require heading uniqueness (spec edge case);
  entries stay in append order.
- An entry missing its paragraph (heading with an empty body) is a malformed entry
  under this contract; the agent-behavior evaluation (SC-005) scores such entries as
  failing the "specifically and accurately describes what was done" criterion.

## `index.md` Catalog Entry

One line per article, under its thematic heading (thematic grouping is an existing,
unchanged convention — this feature does not standardize section headings, only the
per-article line).

```markdown
- [Article Title](relative/path/to/article.md) — <short description> — <source-status marker>
```

- Link: relative path from the content root to the article file.
- Description: short prose, written in the wiki's configured content language
  (German by default, or the operator's configured language — spec Clarifications;
  this is agent-generated wiki content, not repository code/docs, so CLAUDE.md's
  English-only policy does not apply here).
- Source-status marker: a source count (e.g. `3 Quellen`) or a stub indicator (e.g.
  `Stub — keine Quellen`) for an article with no sourced content yet (spec edge case) —
  the agent's own judgment, not a separately tracked structured field (spec
  Assumptions).
- Identical shape regardless of which agent type adds or updates the entry (FR-013).
  Lint never writes `index.md` (no write rule for it in `data/agents/lint/policy.json`)
  so it never produces a catalog entry.

## Non-goals

- No YAML/structured re-parsing of either format is introduced — both stay
  human-readable markdown, matching the existing `log.md`/`index.md` convention.
- No uniqueness, ordering, or cross-reference validation is added beyond what FR-007–FR-013
  state above.
