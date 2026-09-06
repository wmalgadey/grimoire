---
name: wiki-identity-reset
description: Revert a Grimoire instance from a specialised foundation document back to the shipped default. Use when an operator wants to undo a previously set specialised wiki-identity, asks to "reset the wiki identity" or "go back to default", or reports that `wiki-identity set --default` didn't change anything. Only for a Claude Code session running ON the deploy host, where `grimoire-server` is available.
allowed-tools: Bash, Read
---

# wiki-identity-reset — Undo a specialised wiki-identity

`wiki-identity set --default` never removes an instance document that is already in place —
that is deliberate, not a gap: the wizard has no menu entry that deletes one, so it can never
leave an instance with less identity than it had (@deploy/server/README.md's `wiki-identity`
section has the exact wording). Reverting to the shipped default is a separate, deliberate
file operation instead — `grimoire-server wiki-identity-reset` — and this skill is that
operation done carefully, with a human confirming before anything is removed.

## Are you on the deploy host?

If `grimoire-server` is not on `PATH` and `deploy/server/grimoire-server` does not exist in
this checkout, this is not the deployment host — say so instead of doing anything.

## Step 1 — Check what is actually in place

```bash
grimoire-server wiki-identity-reset
```

Run it bare first, without `--yes` — it refuses by design and prints the instance document's
`source`, `resolved_path`, `sha256` and `heading` before dying. If it reports
"Already on the shipped default — nothing to remove", stop here: there is nothing to do, and
saying so is the correct outcome, not a failure.

## Step 2 — Confirm with the operator before removing anything

This is irreversible: the instance document is deleted, not archived, and nothing downstream
keeps a copy. Show the operator exactly what Step 1 printed (the heading and path are usually
enough to recognise it) and ask them to confirm they want it gone — never infer this from
context, and never chain straight from Step 1 into Step 3 without an explicit yes. If it took
real drafting effort to produce, suggest saving a copy first:

```bash
grimoire-server wiki-identity-reset >/tmp/wiki-identity-backup-context.txt 2>&1
grimoire-server wiki-identity set --from-file /path/to/a/backup/you/already/have --replace
```

(That second line only applies if the operator already has the drafted document saved
somewhere outside the running instance — there is no command that reads the instance
document back out, so a backup only exists if one was kept at drafting time.)

## Step 3 — Remove it

Only after the operator's explicit go-ahead:

```bash
grimoire-server wiki-identity-reset --yes
```

## Step 4 — Verify

```bash
grimoire-server wiki-identity
```

`source: default` confirms it took — with no restart needed, the very next Ingest, Query, or
Lint run resolves the shipped default again.
