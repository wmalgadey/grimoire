# Convention: Wiki Content Root Composition

**Status**: Active | **Introduced by**: 022-align-wiki-structure | **Related**: ADR-007,
ADR-017, ADR-022, ADR-023

This is the single authoritative description of what lives in the wiki content root. Every
agent system prompt restates it; every structural rule enforces it; every other document that
needs it cites this one.

## Why restatement rather than reference

ADR-007 fixes the instruction surface at one `system-prompt.md` per agent, loaded verbatim, and
rejected a structural split precisely so that editing one file provably edits the whole system
prompt. There is no include mechanism, so the three prompts cannot reference this file at
runtime — they restate it, and `WikiContentTerminologyRuleTests` /
`RetiredPagesWrapperPathRuleTests` check they have not drifted from it.

## C1 — Root composition

The wiki content root contains exactly three kinds of thing:

- `index.md` — the catalog
- `log.md` — the append-only activity log
- topical **category folders**, each holding **articles**

No wrapper directory sits between the content root and a category folder. An article's path is
`<category>/<slug>.md`.

```text
<content root>/
├── index.md        # the catalog — every article, linked by content-root-relative path
├── log.md          # the append-only activity log
├── tech/           # Technologies, platforms
├── tools/          # Tools, CLIs, SaaS products
├── concepts/       # Abstract concepts, patterns, ideas
├── events/         # Conferences, events
├── people/         # Named individuals
├── organisations/  # Companies, projects, communities
├── hobbies/        # Non-technical interests
├── personal/       # Personal reflections and notes
└── sources/        # Source summaries
```

## C2 — Categories are open-ended

The category set is **not** fixed by the harness. The folders above are illustrative, not
exhaustive. An agent creates a new category folder when none of the existing ones fits, and only
then.

## C3 — Reserved harness surfaces

Four top-level names are reserved and harness-owned:

| Name | Contains |
|------|----------|
| `tasks` | Task artifacts |
| `conversations` | Query conversation records |
| `findings` | Lint findings reports |
| `remediation-tasks` | Remediation task records |

They record what the agents did and let the operator see and interact with them. They are **not
wiki content**:

- never an article category
- never a write target for an agent
- never citable as a source for a wiki answer
- never derivable into an article

Whether an agent may **read** them is the operator's decision, made per surface in
`Grimoire:HarnessSurfaceReads` and **denied by default** (ADR-023). A denied read is routine: it
is recorded with reason `harness_surface_not_granted` and the run continues with the wiki
content it can reach.

Granting a surface widens what an agent knows, not what counts as wiki knowledge — a granted
surface may be read for context and to answer questions about what happened, but never cited as
a wiki source and never derived into an article.

## C4 — Discovery

An agent discovers what the wiki contains by enumerating the content root directly —
`list_files(".")` — and then the category folders it reveals. **This is permitted by every
shipped policy**; the read scope grants the content root.

`index.md` is a convenience, not a prerequisite. When it is absent, the agent enumerates and
reports the missing catalog as a gap — never as evidence that the wiki is empty.

## C5 — Reference forms

- **Catalog entries** in `index.md` use a markdown link to the article's content-root-relative
  path:

  ```markdown
  - [Title](category/slug.md) — description — status
  ```

  This is structurally enforced (ADR-017): a new `- [`-led line failing
  `^- \[.+\]\(.+\) — .+ — .+$` is denied `catalog_entry_malformed`. A wikilink cannot satisfy it.

- **Everywhere else** — body prose, frontmatter values, log paragraphs — use a wikilink.

- **Wikilinks resolve by filename**, folder-agnostic. `[[slug]]` and `[[category/slug]]` name the
  same article; only the final path segment is significant. Resolve one against the article
  filenames you enumerated — do not construct a path from it.

## C6 — Empty and partial roots

- A root with no articles is reported as having no articles yet, describing what was found.
- A root missing `index.md` or `log.md` does not fail a run. When an agent next needs to write a
  catalog or log entry, it creates the missing file as part of that write. ADR-017's append-only
  check already exempts the first write to a file that does not yet exist.
- The absence of any particular folder is never offered as the reason the wiki is empty.

## C7 — Retired vocabulary

The `pages/` wrapper folder does not exist. It was retired by feature 014 and must not appear in
any instruction, path, or current-state description.

**"Article" is the canonical term** for a unit of wiki content. "Page" is retired as project
terminology — in prose, in identifiers, in metric names, in persisted record fields.

A content root encountered with a literal `pages/` directory is an ordinary category folder
holding articles. Read it like any other; it is not special.

## Enforcement

| Rule | Mechanism |
|------|-----------|
| No prompt, doc, or comment reintroduces the retired path concept | `RetiredPagesWrapperPathRuleTests` |
| No identifier, metric, or persisted field uses the retired term | `WikiContentTerminologyRuleTests` |
| This document and the test fixtures do not drift apart | Fixture-mirror `[Fact]` in both rule classes |
| Accepted decision records documenting the retirement are not flagged | Historical-marker exemption, pinned by its own `[Fact]` |
| The reserved surface names are declared once | ADR-023 rule H2, `HarnessSurfaceScopeRuleTests` |

## Exemption list

Paths excluded from the scans in both rule classes, one per row, each justified. This list is
mirrored by an in-test fixture in `RetiredPagesWrapperPathRuleTests` and
`WikiContentTerminologyRuleTests`; a drift between this document and either fixture, in either
direction, fails the build.

| Exempted path | Justification |
|---------------|----------------|
| `bin` | Build output |
| `obj` | Build output |
| `node_modules` | Dependency build output |
| `.svelte-kit` | Framework build output |
| `.git` | Version control internals |
| `.grimoire` | Build output of `PublishAgentRuntime`; scanning it double-reports every instruction violation already caught at its source under `backend/src` |
| `frontend` | SvelteKit's `+page.svelte` / `entries/pages/` are framework filenames with unrelated meaning |
| `Grimoire.AgentEvals/Fixtures/recordings` | Frozen ADR-012 replay transcripts |
| `foundational` | Absorbed source material per CLAUDE.md's Document Map ("No — never cite as requirements") — historical input, not live documentation this convention governs |
| `ideas` | Same Document Map classification — prompt libraries and exploratory notes, not live documentation |

**Self-referential exemption** (not a mirrored path, a single named file): this document itself,
`docs/conventions/wiki-content-root.md`, is exempt wholesale from both rules. C7 exists to say
"page" is retired, which requires using the word — the same way a style guide banning a term
must print the term. Every other live document is expected to comply outright.

**Historical-marker exemption** (not a path exemption): a line under `docs/adr/` or `specs/` is
exempt from both rules when it or its immediate context carries a retirement marker (`retired`,
`superseded by`, `As of 014-wiki-storage-restructure`) — a record of a past decision, not a
current-state description (FR-010).

Terminology exemption (not a path exemption, `WikiContentTerminologyRuleTests` only): frontmatter
`type:` values (data already written into existing wiki articles); the machine-read keys
`targetPath`, `inbound_links`, `superseded_by`, `supersedes`, `confidence`, `confidence_reason`,
`last_reviewed`, `review_date`; OTel service names, `*_agent.*` span prefixes, `task_id`,
`turn_id` (ADR-013 frozen identities).
