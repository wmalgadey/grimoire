# Contract: Wiki Content Root Composition

**Feature**: 022-align-wiki-structure

This contract defines what `docs/conventions/wiki-content-root.md` must state and what every
agent system prompt must restate. It is the SC-005 "exactly one place" artifact.

## Why restatement rather than reference

ADR-007 fixes the instruction surface at one `system-prompt.md` per agent, loaded verbatim, and
rejected a structural split precisely so that "editing one file provably edits the whole system
prompt". There is no include mechanism and adding one would reopen that decision. So the three
prompts cannot *reference* a shared file at runtime — they restate it, and an architecture test
checks they have not drifted from the document.

## Normative content

The document and each prompt's restatement MUST state all of the following.

### C1 — Root composition

The wiki content root contains exactly three kinds of thing:

- `index.md` — the catalog
- `log.md` — the append-only activity log
- topical category folders, each holding articles

No wrapper directory sits between the content root and a category folder. An article's path is
`<category>/<slug>.md`.

### C2 — Categories are open-ended

The category set is not fixed by the harness. Illustrative categories: `tech`, `tools`,
`concepts`, `events`, `people`, `organisations`, `hobbies`, `personal`, `sources`. An agent
creates a new category folder when none of the existing ones fits, and only then.

### C3 — Reserved harness surfaces

Four top-level names are reserved and harness-owned: `tasks`, `conversations`, `findings`,
`remediation-tasks`. They record what the agents did and let the operator see and interact with
them. They are **not** wiki content:

- never an article category
- never a write target for an agent
- never citable as a source for a wiki answer
- never derivable into an article

Whether an agent may read them at all is the operator's decision, denied by default. An agent
must treat a denial as routine and continue with the wiki content it can reach.

### C4 — Discovery

An agent discovers what the wiki contains by enumerating the content root directly —
`list_files(".")` — and then the category folders it reveals. This is permitted by every shipped
policy. `index.md` is a convenience, not a prerequisite: when it is absent, the agent enumerates
and reports the missing catalog as a gap, never as evidence that the wiki is empty.

### C5 — Reference forms

- **Catalog entries** in `index.md` use a markdown link to the article's content-root-relative
  path: `- [Title](category/slug.md) — description — status`. Structurally enforced (ADR-017);
  a wikilink here is denied `catalog_entry_malformed`.
- **Everywhere else** — body prose, frontmatter values, log paragraphs — use a wikilink.
- **Wikilinks resolve by filename**, folder-agnostic: `[[slug]]` and `[[category/slug]]` name
  the same article, and only the final segment is significant. Resolve one against the article
  filenames you enumerated; do not construct a path from it.

### C6 — Empty and partial roots

- A root with no articles is reported as having no articles yet, describing what was found.
- A root missing `index.md` or `log.md` does not fail a run. When an agent next needs to write
  a catalog or log entry, it creates the missing file as part of that write.
- The absence of any particular folder is never offered as the reason the wiki is empty.

### C7 — Retired vocabulary

The `pages/` wrapper folder does not exist and must not appear in any instruction, path, or
current-state description. "Article" is the canonical term for a unit of wiki content; "page" is
retired.

A content root encountered with a literal `pages/` directory is an ordinary category folder
holding articles — read it like any other, do not treat it as special.

## Enforcement

| Rule | Mechanism |
|------|-----------|
| No prompt, doc, or comment reintroduces the retired path concept | `RetiredPagesWrapperPathRuleTests` |
| No identifier, metric, or persisted field uses the retired term | `WikiContentTerminologyRuleTests` |
| The document and the test fixture do not drift apart | Fixture-mirror `[Fact]`, modelled on `AgentArtifactNamingRuleTests.ExemptionFixture_MustMirror_TheConventionDocument` |
| Accepted decision records documenting the retirement are not flagged | Historical-marker exemption, verified by its own `[Fact]` with a synthetic ADR fragment |
