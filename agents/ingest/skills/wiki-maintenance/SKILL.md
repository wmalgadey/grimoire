# Skill: Wiki Maintenance

This skill defines the conventions for every page you create or edit in the Grimoire
wiki. Apply these rules to all `write_file` calls that target `wiki/pages/`.

---

## Page Types

| Type | When to create | File location |
|------|---------------|---------------|
| **Concept** | Abstract ideas, principles, design patterns | `wiki/pages/<slug>.md` |
| **Technology** | Tools, platforms, libraries, frameworks | `wiki/pages/<slug>.md` |
| **Person** | Named individuals (authors, researchers, practitioners) | `wiki/pages/<slug>.md` |
| **Organisation** | Companies, projects, communities | `wiki/pages/<slug>.md` |
| **Source summary** | Condensed representation of a specific source document | `wiki/pages/sources/<slug>.md` |

A single source may produce pages of multiple types (e.g. a book produces a source
summary page, a concept page, and an author person page).

---

## Frontmatter Standard

Every wiki page except `index.md` and `log.md` requires this YAML frontmatter block:

```yaml
---
tags:
  - tech/ExampleTech
  - concept/ExampleConcept
confidence: medium
confidence_reason: "One authoritative source; no corroboration yet."
inbound_links: 0
last_reviewed: YYYY-MM-DD
---
```

Optional fields (add when applicable):

```yaml
superseded_by: "[[new-page-slug]]"   # only when this page is being superseded
supersedes: "[[old-page-slug]]"       # only when this page replaces an older one
```

Set `last_reviewed` to the date of this ingest run.

Do **not** omit frontmatter. A page without frontmatter is malformed.

---

## Tag Taxonomy

Tags use prefixed namespaces. Use at least **2 tags per page** (one category prefix + one
content-specific tag).

| Prefix | Covers | Examples |
|--------|--------|---------|
| `person/` | Named individuals | `person/Simon-Wardley`, `person/Andrej-Karpathy` |
| `company/` | Organisations, projects | `company/Anthropic`, `company/Microsoft` |
| `tech/` | Technologies, platforms, tools | `tech/dotnet`, `tech/Kubernetes`, `tech/SQLite` |
| `pattern/` | Architecture / design patterns | `pattern/DDD`, `pattern/GitOps`, `pattern/CQRS` |
| `concept/` | Abstract concepts, principles | `concept/AI-Safety`, `concept/Platform-Engineering` |
| `source-type/` | Nature of the source | `source-type/book`, `source-type/official-docs`, `source-type/blog`, `source-type/synthesis` |

Introduce new prefixes only when none of the above fits.

---

## Confidence Scoring

Calculate confidence when creating or updating a page. Record the score as
`high`, `medium`, or `low` and a brief human-readable reason.

**Scoring:**

| Signal | Points |
|--------|--------|
| 3 or more independent sources | +1 |
| Source is a book or official documentation | +1 |
| Source is a LinkedIn / X / blog post | −1 |
| Page contains an explicit contradiction marker (⚠️) | −1 |
| Source is older than 18 months and covers a fast-moving topic | −1 |
| Inbound links ≥ 3 (checked at lint time, not ingest time) | +1 |
| Inbound links = 0 (orphan) | −1 |

**Thresholds:** total ≥ 2 → `high` | 0–1 → `medium` | < 0 → `low`

Set `inbound_links: 0` on new pages; lint runs update it separately.

---

## Supersession Rules

Supersede an existing page when the new source **clearly replaces** it — not just adds to
it. Clear replacement means: a newer version, a significant correction, or an explicit
statement that the old information is obsolete.

**When superseding, apply both edits atomically (same run):**

On the **old page** — add to frontmatter:
```yaml
superseded_by: "[[new-page-slug]]"
confidence: low
```
Add a visible notice at the top of the body:
```
> ⚠️ This page has been superseded by [[new-page-slug]] (YYYY-MM-DD).
```

On the **new page** — add to frontmatter:
```yaml
supersedes: "[[old-page-slug]]"
```

Do not delete old pages. The history is valuable. Low confidence + `superseded_by`
signals that the content is stale without losing it.

---

## Catalog (index.md) Upkeep

`wiki/index.md` is the human- and agent-readable entry point to the wiki. Keep it
current:

- Add a line for every new page you create:
  `- [[pages/<slug>]] — <one-sentence summary>`
- Group entries under thematic headings (add headings as needed).
- If a page you updated now covers a significantly different scope, update its summary
  line.
- Do not remove entries for superseded pages — add `(superseded)` to their summary line.

---

## Ingest Log (log.md) Upkeep

Append one log entry per run, at the **end** of `wiki/log.md`:

```
## [YYYY-MM-DD] ingest | completed | source: <source-ref> | <what changed> | task: [[tasks/<task_id>.md]]
```

For a failed run the harness appends its own minimal entry; you do not need to handle
that case.

For a run with supersession, include `superseded [[old-page-slug]] with [[new-page-slug]]`
in the `<what changed>` part.

---

## Contradiction Marking

When a source contradicts an existing page without clearly resolving the contradiction,
mark both the existing and new content:

```
> ⚠️ Contradiction with [[other-page]]: <one-line description of the conflict>
```

This is different from supersession: use contradiction when it is unclear which source is
correct. Use supersession when the newer source clearly wins.
