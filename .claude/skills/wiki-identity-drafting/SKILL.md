---
name: wiki-identity-drafting
description: Draft a specialised foundation document for a Grimoire instance's wiki-identity wizard and hand it back via grimoire-server. Use when an operator wants this instance to maintain a specific kind of wiki instead of Grimoire's shipped default, or after running `grimoire-server wiki-identity set --specialised` and receiving a drafting brief. Only for a Claude Code session running ON the deploy host, where `grimoire-server` is available.
argument-hint: "Optional: the operator's own description of the wiki this instance should maintain"
allowed-tools: Bash, Read, Write, Grep, Glob
---

## User Input

```text
$ARGUMENTS
```

If this is non-empty, it **is** the operator's own description — skip straight to Step 1's
`grimoire-server` invocation with it, verbatim. If it's empty, Step 1 tells you to ask.

# wiki-identity-drafting — Draft the document the wizard asked for

This skill belongs to the Claude Code session running on the deployment host — the same
one that runs `grimoire-server` day to day. The wizard (`wiki-identity`) never drafts
content itself: it hands out a brief and takes a document back. Drafting is agent
judgment, and this session is the agent. The command's own contract lives in
@deploy/server/README.md's `wiki-identity` section; this skill is the do-it-now version
of the same three steps.

## Are you on the deploy host?

If `grimoire-server` is not on `PATH` and `deploy/server/grimoire-server` does not exist in
this checkout, this is not the deployment host — say so instead of drafting anything.

## Step 1 — Get the brief

If `$ARGUMENTS` was empty and the operator hasn't otherwise told you what kind of wiki this
instance should maintain, ask them first — that's the one question this whole skill exists
to answer, and it isn't yours to guess or default. Once you have their own description in
their own words, ask for the brief yourself rather than making them run a command:

```bash
grimoire-server wiki-identity set --specialised --description "<the operator's own description, verbatim>"
```

Use `--description -` and pipe a long description through stdin instead of a shell
argument if it is more than a line or two. This writes nothing — it prints a brief that
now tells you everything Step 2 needs: the required shape, the voice, the role/foundation
boundary (with an example), and a worked-example file to read if anything is unclear.
Follow what it says rather than what's summarized here.

The brief names its own command as bare `wiki-identity` because that's what it's called
inside the Hub. On the deploy host it isn't a command by itself — prefix it with
`grimoire-server`, exactly as Step 3 below does.

## Step 2 — Draft the document

This is the actual judgment call, not the wizard's. Write the markdown file the brief
describes, exactly as it describes it — nothing more, nothing role-specific. If what the
operator actually wants turns out to be role behaviour rather than a wiki-wide convention
(the brief gives an example of the difference), there is no home for that yet; say so
rather than forcing it in.

## Step 3 — Hand it back and verify

Check first whether this instance already has one in place — don't guess:

```bash
grimoire-server wiki-identity
```

`source: instance` means it does. Only then decide: add `--replace` below if the draft is
meant to overwrite what's there; leave it off if `source: default` (nothing to overwrite)
or if an existing document should be left alone.

```bash
grimoire-server wiki-identity set --from-file /tmp/foundation-draft.md
```

Without `--replace` on an existing document the command refuses with `StateConflict` and
leaves the bytes on disk untouched — that's the safety net if the check above was wrong,
not a substitute for doing it.

Then confirm the hand-back took by running `grimoire-server wiki-identity` again:
`source: instance` and a `heading` matching the draft's own first heading mean it's live —
with no restart needed, the very next Ingest, Query, or Lint run operates under it.
