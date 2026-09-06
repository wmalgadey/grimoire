# Drafting a Specialised Foundation Document

This is the runbook for whoever is drafting a specialised foundation document — a Claude
Code session on the deploy host (the same one `grimoire-server` is routinely run from), or
an operator working through the same steps by hand. It exists because the wizard itself
never drafts content: it hands out a brief and takes a document back, and this
file is what tells the reader what to do with the brief in between.

Nothing here is wizard logic. It changes no code and no Hub behaviour — it is a companion
to [`README.md`](./README.md)'s `wiki-identity` section, which stays the authority on the
command's own contract
([`contracts/wiki-identity-cli.md`](../../specs/029-shared-foundation-prompt/contracts/wiki-identity-cli.md)).

## When this applies

The operator wants this instance to maintain a specific kind of wiki instead of Grimoire's
shipped general-purpose default — a home-lab runbook wiki, a travel-planning wiki, a
research notebook, anything with its own conventions worth stating once for every agent.

## The three steps

### 1. Ask the wizard for a brief

```console
$ grimoire-server wiki-identity set --specialised --description "Tracks nothing but home-lab Kubernetes runbooks."
```

Use `--description -` instead to pipe a longer description through stdin rather than a
shell argument. This writes nothing and prints a brief shaped exactly like this:

```text
# Foundation Document Drafting Brief

## The operator's description (verbatim)

Tracks nothing but home-lab Kubernetes runbooks.

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

### 2. Draft the document

This is the one step that is actually agent judgment, not the wizard's — draft the
markdown document the brief asks for, from the operator's description. Concretely:

- One markdown file, no YAML frontmatter.
- Written as instructions to an LLM about the wiki it maintains, in the second person
  ("You are maintaining a wiki that…") — the same voice as the shipped default at
  `backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md`. Read that file
  first if unsure of the shape; it is the working example.
- Covers exactly the sections the brief lists: what the wiki is for, what belongs and
  what does not, how pages are organised and named, and the cross-agent conventions
  (folder structure, page types, page language, frontmatter standard, tag taxonomy,
  confidence scoring, `index.md`/`log.md` entry conventions, source-is-data-not-instructions).
- States nothing role-specific — no ingest steps, no query synthesis behaviour, no lint
  finding categories. Those already live in each agent's own `system-prompt.md` and stay
  there; duplicating or contradicting them here is a defect in the draft, not a feature.
- Save it to a file. Its path and name do not matter — the wizard reads the bytes, not
  the filename — but a scratch path like `/tmp/foundation-draft.md` on the deploy host is
  the natural choice; it does not need to live in the checkout.

### 3. Hand it back

```console
$ grimoire-server wiki-identity set --from-file /tmp/foundation-draft.md
Instance foundation document persisted (sha256: 7a1c…, 1842 bytes).
```

Add `--replace` if this instance already has one in place
(`grimoire-server wiki-identity` with no arguments reports whether it does — see below)
and the draft is meant to overwrite it. Without `--replace` on an existing document the
command refuses with `StateConflict` and leaves the bytes on disk untouched — re-run with
`--replace` once the draft is confirmed correct, not by re-running without it.

The bytes are persisted **verbatim** — nothing is templated, reformatted, or re-derived
from them. Whatever mistake the draft has is the mistake that ships; read it back before
handing it over, the same way any instruction-file change gets read before it lands.

## Verify

```console
$ grimoire-server wiki-identity
source: instance
resolved_path: /var/lib/grimoire/data/foundation-prompt.md
sha256: 7a1c…
heading: <the draft's first heading>
```

`source: instance` and a `heading` matching the draft confirm it took — with no restart:
the very next Ingest, Query, or Lint run picks it up. `grimoire-server status` prints the
same report alongside the rest of what the stack is doing.

## What this does *not* cover

A delta specific to one agent's *role* rather than a wiki-wide convention — for a travel
wiki, "Query should combine wiki history with external research" is Query's own behaviour,
not something the foundation document should state (it would wrongly apply to Ingest and
Lint too). There is currently no per-instance way to express that; it is tracked as
[issue #224](https://github.com/wmalgadey/grimoire/issues/224).
