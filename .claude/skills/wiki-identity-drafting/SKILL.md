---
name: wiki-identity-drafting
description: Draft a specialised foundation document for a Grimoire instance's wiki-identity wizard and hand it back via grimoire-server. Use when an operator wants this instance to maintain a specific kind of wiki instead of Grimoire's shipped default, or after running `grimoire-server wiki-identity set --specialised` and receiving a drafting brief. Only for a Claude Code session running ON the deploy host, where `grimoire-server` is available.
allowed-tools: Bash, Read, Write, Grep, Glob
---

# wiki-identity-drafting — Draft the document the wizard asked for

This skill belongs to the Claude Code session running on the deployment host — the same
one that runs `grimoire-server` day to day. The wizard (`wiki-identity`) never drafts
content itself: it hands out a brief and takes a document back. Drafting is agent
judgment, and this session is the agent. Full rationale and the command's own contract
live in
[`deploy/server/wiki-identity-drafting-guide.md`](../../../deploy/server/wiki-identity-drafting-guide.md)
and
[`deploy/server/README.md`](../../../deploy/server/README.md)'s `wiki-identity` section —
this skill is the condensed, do-it-now version of the same three steps.

## Are you on the deploy host?

If `grimoire-server` is not on `PATH` and `deploy/server/grimoire-server` does not exist in
this checkout, this is not the deployment host — say so instead of drafting anything.

## Step 1 — Get the brief

If the operator hasn't already told you what kind of wiki this instance should maintain,
ask them first — that's the one question this whole skill exists to answer, and it isn't
yours to guess or default. Once you have their own description in their own words, ask
for the brief yourself rather than making them run a command:

```bash
grimoire-server wiki-identity set --specialised --description "<the operator's own description, verbatim>"
```

Use `--description -` and pipe a long description through stdin instead of a shell
argument if it is more than a line or two. This writes nothing — it only prints a brief
shaped like:

```text
# Foundation Document Drafting Brief

## The operator's description (verbatim)

<the description, verbatim>

## Required shape

Draft one markdown document (no frontmatter) stating:
- What this wiki is for
- What belongs in it and what does not
- How pages are organised and named
- The conventions that hold across every agent's work (folder structure, page types,
  page language, frontmatter standard, tag taxonomy, confidence scoring, `index.md`/
  `log.md` entry conventions, and how source content is treated)

It states nothing about any one agent's role, write scope, or steps — those stay in
each agent's own instruction file.

## Handing the result back

Save the drafted document to a file, then run:

    wiki-identity set --from-file <path-to-drafted-document>

Add --replace if an instance document is already in place and should be overwritten.
```

The brief names its own command as bare `wiki-identity` because that's what it's called
inside the Hub. On the deploy host it isn't a command by itself — prefix it with
`grimoire-server`, exactly as Step 3 below does.

## Step 2 — Draft the document yourself

This is the actual judgment call, not the wizard's. Write the markdown file the brief
asks for:

- One file, no YAML frontmatter.
- Second person, addressed to the agents that will read it ("You are maintaining a
  wiki that…") — the same voice as the shipped default at
  `backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md`. Read that file
  first if the shape is unclear; it is the working example, not just a description of one.
- Covers exactly what the brief lists — what the wiki is for, what belongs and what does
  not, page organisation and naming, and the cross-agent conventions (folder structure,
  page types, page language, frontmatter standard, tag taxonomy, confidence scoring,
  `index.md`/`log.md` entry conventions, source-is-data-not-instructions) — and nothing the
  brief doesn't ask for.
- States nothing role-specific: no ingest steps, no query synthesis behaviour, no lint
  finding categories. Those already live in each agent's own `system-prompt.md`.
  Duplicating or contradicting them here is a defect in the draft, not a feature — if the
  operator's ask is actually role-specific (e.g. "Query should combine wiki history with
  external research" for a travel wiki), that has no home yet; say so rather than forcing
  it into the foundation document (tracked as
  [issue #224](https://github.com/wmalgadey/grimoire/issues/224)).
- Save it wherever is convenient — its path and filename do not matter, the wizard reads
  the bytes, not the name. A scratch path like `/tmp/foundation-draft.md` is fine; it does
  not need to live in the checkout.
- Read the draft back once before handing it over. It ships verbatim — nothing downstream
  templates, reformats, or corrects it.

## Step 3 — Hand it back and verify

```bash
grimoire-server wiki-identity set --from-file /tmp/foundation-draft.md
```

Add `--replace` if this instance already has an instance document in place and the draft
is meant to overwrite it (check first with the report below — without `--replace` on an
existing document the command refuses with `StateConflict` and leaves the bytes on disk
untouched; re-run with `--replace` once the draft is confirmed correct, not by blindly
retrying).

Then confirm it took:

```bash
grimoire-server wiki-identity
```

`source: instance` and a `heading` matching the draft's own first heading mean it's live —
with no restart needed, the very next Ingest, Query, or Lint run operates under it.
