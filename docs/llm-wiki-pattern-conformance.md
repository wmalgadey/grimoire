---
role: conformance-analysis
status: point-in-time (2026-08-18)
binding: none — never cite as requirements; findings are binding only once filed as issues and taken through the spec-kit workflow
sdd_usage: input to /speckit-specify for issues #109–#113; re-run when the agent instruction files or the upstream pattern change
sources_compared:
  - docs/foundational/llm-wiki-nanoclaw-idea.md (Karpathy's pattern; verified byte-identical to upstream on 2026-08-18)
  - wmalgadey/nanoclaw .claude/skills/add-karpathy-llm-wiki/SKILL.md (activation skill, main @ 2026-08-18)
  - docs/foundational/llm-wiki-magrathea-claude.md (the deployed schema layer)
  - docs/foundational/llm-wiki-magrathea-skill.md (the deployed wiki skill)
  - backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/*.md (1043 lines, 2026-08-18)
---

# LLM-Wiki Pattern Conformance Analysis

**Date**: 2026-08-18 · **Question asked**: are Grimoire's heavily adapted agent
instructions still compatible with the original LLM-Wiki idea and the skill that
instantiated it, and where do they deviate?

Quotations from the Magrathea documents are reproduced in German, the language they were
written in; they are evidence of what the source said, not authored content.

## Verdict

Compatible at the core. All three layers and all three operations are recognisably
present, and in several places Grimoire is *stricter* than the pattern rather than weaker.
There is one large, deliberate structural shift (the schema layer), five concrete
detail deviations from the Magrathea skill — three of which look like unintended
regressions — and two open design questions. Every finding below is filed as an issue;
see the traceability table at the end.

## 1. What survives unchanged

- **Three layers** (raw sources / wiki / schema) and **three operations** (ingest, query,
  lint).
- **`index.md`** as a content-oriented catalogue, **`log.md`** append-only with the
  greppable `## [YYYY-MM-DD] type | …` prefix Karpathy specifies.
- **Ingest as an act of integration, not summarisation** — update / supersede / create as
  a judgment call, cross-references maintained on every pass.
- **Contradiction markers (`⚠️`), supersession rules, tag taxonomy** (six prefixes,
  ≥ 2 tags per page): these blocks are near-verbatim descendants of the Magrathea skill.
- **Query may file good answers back into the wiki** — Karpathy's "explorations compound
  in the knowledge base rather than disappearing into chat history".
- **Lint covers Karpathy's full list**: contradictions, stale claims, orphans, missing
  cross-references, scattered concepts, gaps — and goes beyond it with proposals →
  authorization → remediation execution.
- **The one-source-at-a-time discipline**, which the activation skill flags as critical
  ("Never batch-read all files and then process them together"), is no longer an
  instruction at all: one ingest task is one source, enforced by the harness. That is a
  stronger realisation of the same intent.

## 2. The structural deviation: ownership of the schema layer

In the source pattern the schema layer is the artifact the user co-designs and keeps
editing in their own workspace. The activation skill is explicit — "Create
`container/skills/wiki/SKILL.md` tailored to this user's wiki. […] **Don't over-prescribe**
— the pattern says 'your LLM figures out the rest.'" — as is Karpathy's file: "Directory
structure, schema conventions, page formats, tooling—all depend on domain, preferences,
and LLM choice."

Grimoire moved that layer into three versioned `system-prompt.md` files compiled into the
agent binaries, and made it substantially more prescriptive: an OKF v0.1 bundle contract,
an exact `type:` value table, a fixed folder list, a fixed tag taxonomy, and an `index.md`
line shape the guarded write boundary enforces structurally (`catalog_entry_malformed`).

This is **not** a Principle V violation — judgment still lives in instruction files — but
it changes who owns wiki conventions: changing one is a release, not a file edit, and
every Grimoire wiki necessarily looks alike. Filed as **#112**.

Two consequences follow from the same boundary:

- **No shell, no CLI tools.** The agent has exactly `list_files` / `read_file` /
  `write_file`. The activation skill's URL handling (`curl -sLo sources/…`) is replaced
  harness-side by a fetch port, converters and `SourceArtifactStore` — functionally
  cleaner. But Karpathy's explicit scaling answer ("at small scale the index suffices, but
  as the wiki grows, proper search becomes necessary", qmd, "so LLMs can shell out") was
  designed out without a replacement. See §4.
- **The raw-source layer is invisible to agents.** A source reaches an ingest run only
  inline in the `<source>` block of its own kickoff message
  (`Grimoire.AgentRuntime/Core/AgentLoop.cs:214-229`). Raw sources are persisted, but for
  the Hub and humans only. Magrathea could list the Zettelkasten; that is what made its
  `/batch` clustering and its "Lücken: wichtige Themen die im Zettelkasten fehlen" finding
  possible. Filed as **#113**.

A third consequence is the loss of the human-in-the-loop step: Karpathy's ingest "reads
sources, **discusses takeaways**, writes summaries", and Magrathea instructs "nach jeder
Datei kurz die wichtigsten Erkenntnisse nennen, dann fragen ob weiter". Grimoire's ingest
is non-interactive, with a mandatory final summary into the task artifact instead. Query
has conversation and Lint has Message-Turn Mode; ingest has nothing comparable. Already
covered by **#72**.

## 3. Detail deviations from the Magrathea skill

| # | Deviation | Assessment | Issue |
|---|---|---|---|
| 1 | `inbound_links` is a required frontmatter field in the source skill and the only field Lint may write — but the Ingest prompt never mentions it | regression | #109 |
| 2 | `last_reviewed` likewise required at ingest time in the source skill; Grimoire sets only `timestamp`, so Lint's 90-day window measures ingest age, not review age | regression | #109 |
| 3 | Confidence formula lost its two inbound-link signals (`≥ 3 → +1`, orphan → `−1`) while keeping the original thresholds, so the attainable maximum fell from +3 to +2 and `high` now requires both positive signals with no negatives | regression | #110 |
| 4 | The `sources/<slug>.md` summary — the *first* artifact of every ingest in the source skill — is now merely a page type that nothing requires, so citations can dangle and the raw layer loses its in-wiki trace | regression | #111 |
| 5 | "One source typically touches 5–15 pages" vs Karpathy's "10-15 wiki pages" — softens the expectation exactly where the pattern argues hardest ("LLMs … can touch 15 files in one pass") | worth confirming | #111 |

Two further changes are deliberate and sound, recorded here so they are not re-litigated:

- **Query's write scope was narrowed** to create-only Synthesis Pages with an explicit
  synthesis test and the standing right to decline a "save this" request. Karpathy's
  "good answers can be filed back" is preserved with more discipline.
- **Lint gained a remediation lifecycle** (proposal → human authorization → execution),
  an extension of the source skill's "Biete nach dem Report an, Probleme zu beheben".

## 4. Did our deviations cause #108?

[#108](https://github.com/wmalgadey/grimoire/issues/108) reports that Lint's "read the
whole wiki" no longer fits one context: 633 pages, ~1.63 M characters, ~400 k tokens.

**Partly, yes.** The deviations did not build the wall, but they removed both escape
hatches the pattern provided and added a mandate that maximises context use.

1. **The removed shell is the most direct contributor.** The Magrathea skill solves
   exactly the task #108 dies on, at zero context cost:
   `grep -rl "\[\[seitenname" /workspace/agent/llm-wiki/ --include="*.md" | wc -l`.
   Grimoire replaced it with the two-pass in-context tally
   (`Grimoire.LintAgent/Instructions/system-prompt.md:165-186`), which forces every page
   body plus an extraction transcript into the conversation. #42 is the accuracy side of
   the same substitution: an exact mechanical operation became an approximated one
   (90 % < 95 %).
2. **"Read the whole wiki" is a Grimoire addition.** Neither Karpathy nor the Magrathea
   skill demands it; for navigation Karpathy says the opposite — "read the index first to
   locate relevant pages before drilling deeper". The Lint prompt makes reading every page
   a MUST and calls a partial run defective (`:24-32`). The Query agent kept index-first;
   only Lint dropped it.
3. **The scale envelope was stated and passed.** "Works surprisingly well at moderate
   scale (~100 sources, ~hundreds of pages)" — 633 pages is beyond it, and the pattern's
   own remedy for that point is the search/CLI layer that the tool boundary excludes.
4. **#109 amplifies it.** Because ingest never writes `inbound_links`, Lint must create
   the field on every page, so a run scoped to only the pages whose link graph changed is
   structurally impossible.

**What is not our fault:** a faithful port hits a limit here too. Judging contradictions
across 633 pages requires material no window holds; the pattern never claims lint fits in
one context.

**Consequence for #108's choice of direction:** Option A (index- and frontmatter-first,
drilling into suspects) is not a workaround — it restores the pattern's own navigation
rule, which argues for doing it first. But A alone does not fix `inbound_links`: the count
needs the mechanical path back, either as harness-computed values or as a link-graph
capability at the guard boundary. That is the point where #108 and #42 share one root
cause.

## 5. Traceability

| Finding | Issue |
|---|---|
| Missing `inbound_links` / `last_reviewed` at ingest time | [#109](https://github.com/wmalgadey/grimoire/issues/109) |
| Confidence formula reduced, thresholds unchanged | [#110](https://github.com/wmalgadey/grimoire/issues/110) |
| `sources/` summary optional; depth expectation lowered | [#111](https://github.com/wmalgadey/grimoire/issues/111) |
| Schema-layer ownership: product-fixed vs per-instance | [#112](https://github.com/wmalgadey/grimoire/issues/112) |
| Raw-source layer invisible to agents | [#113](https://github.com/wmalgadey/grimoire/issues/113) |
| No human-in-the-loop step during ingest | [#72](https://github.com/wmalgadey/grimoire/issues/72) (pre-existing) |
| Lint context exhaustion; link tally reliability | [#108](https://github.com/wmalgadey/grimoire/issues/108), [#42](https://github.com/wmalgadey/grimoire/issues/42) (pre-existing, §4) |
