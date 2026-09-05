# Quickstart: validating the shared foundation prompt and the wiki-identity wizard

**Feature**: 029-shared-foundation-prompt | **Date**: 2026-09-05

Runnable scenarios that prove the feature end to end. Details live in
[contracts/](./contracts/) and [data-model.md](./data-model.md); this file is the run guide.

## Prerequisites

- .NET 10 SDK
- `dotnet build backend/Grimoire.slnx` — the agent build is what delivers `foundation-prompt.md` into
  `.grimoire/agents/<agent-id>/Instructions/`, so nothing below works before a build
- A provider credential is **not** required for any scenario here except the recording refresh

## S1 — The default document reaches every agent

```bash
dotnet build backend/Grimoire.slnx
ls .grimoire/agents/*/Instructions/foundation-prompt.md
```

**Expected**: three files, one per agent, byte-identical to
`backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md`.

```bash
cmp .grimoire/agents/ingest/Instructions/foundation-prompt.md \
    backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md
```

**Expected**: no output (identical).

## S2 — A run operates under both documents, and says which

Dispatch one run of each agent type against a temporary wiki, then read the run's record.

**Expected**: the run's instruction entries name **two** documents — the foundation document first,
then the agent's role document — with different content hashes. Editing the foundation document and
dispatching again changes the first hash and leaves the second untouched.

## S3 — Fail-closed on a missing foundation document

Remove `foundation-prompt.md` from one agent's `Instructions/` directory and dispatch a run of that
agent.

**Expected**: the run fails **before any wiki write**, with a reason naming the foundation document —
not a generic "instructions" failure. The wiki root is byte-identical to before the run.

## S4 — The wizard reports the identity in effect

```bash
dotnet run --project backend/src/Grimoire.Hub -- wiki-identity
```

**Expected**: `default`, the resolved per-agent path, the document's hash and its first heading.
Exit code 0. Nothing written.

## S5 — Choosing the default changes nothing

```bash
find <data-dir> -type f | sort > /tmp/before.txt
dotnet run --project backend/src/Grimoire.Hub -- wiki-identity set --default
find <data-dir> -type f | sort > /tmp/after.txt
diff /tmp/before.txt /tmp/after.txt
```

**Expected**: no difference, exit code 0, and a message saying the instance stays on the shipped
default.

## S6 — Specialising an instance, in two steps

```bash
# Step 1 — get a drafting brief. Writes nothing.
dotnet run --project backend/src/Grimoire.Hub -- \
  wiki-identity set --specialised --description "a personal travel wiki that turns my own trip write-ups into new travel plans"
```

**Expected**: a brief on stdout containing the description **verbatim** plus the document's required
shape. `wiki-identity` still reports `default`; nothing was written.

Hand the brief to an agent session, save what it drafts, then:

```bash
# Step 2 — hand the drafted document back.
dotnet run --project backend/src/Grimoire.Hub -- wiki-identity set --from-file ./drafted.md
dotnet run --project backend/src/Grimoire.Hub -- wiki-identity
```

**Expected**: `instance`, pointing at `<data-dir>/foundation-prompt.md`, whose bytes equal
`drafted.md` exactly. A run dispatched now operates under it — no restart needed.

```bash
cmp ./drafted.md <data-dir>/foundation-prompt.md
```

**Expected**: no output.

## S7 — A re-run never silently clobbers

```bash
dotnet run --project backend/src/Grimoire.Hub -- wiki-identity set --from-file ./other.md; echo "exit=$?"
```

**Expected**: exit code **4** (`StateConflict`), a message naming the document already in place, and
`<data-dir>/foundation-prompt.md` unchanged. With `--replace` appended: exit 0 and the new content.

## S8 — No terminal, no hang

```bash
dotnet run --project backend/src/Grimoire.Hub -- wiki-identity set < /dev/null; echo "exit=$?"
```

**Expected**: exit code **2** (`UsageError`) within seconds, naming the option to supply. It does not
block, and nothing changed.

```bash
dotnet run --project backend/src/Grimoire.Hub -- wiki-identity set --default < /dev/null; echo "exit=$?"
```

**Expected**: exit code 0 — every answer was supplied, so no prompt was needed.

## S9 — The identity survives a restart

Set an instance document (S6), stop the Hub, start it again, and run `wiki-identity`.

**Expected**: still `instance`, same hash. In a container deployment the same holds across
`grimoire-server deploy` and `rollback`, because the data root is volume-backed and neither operation
touches it.

## S10 — The deployment script surfaces, and does not compute

```bash
grimoire-server status
```

**Expected**: the identity line carries exactly what `wiki-identity` reports. Setting an instance
document and re-running `status` changes that line and nothing else.

## S11 — Recording refresh (operator-triggered, needs a provider)

Composition changes the system-prompt hash every recorded scenario was captured against, so the replay
evals report **stale** until they are re-captured:

```bash
dotnet test backend/tests/Grimoire.AgentEvals --filter "Tier=SlowEval"
```

**Expected before refresh**: failures naming the changed fingerprints and the refresh command — this is
the instruction-change merge gate working as designed, not a defect.

**Refresh** (needs a provider credential; run the repository's eval capture workflow), then re-run.

**Expected after refresh**: green, with the recordings carrying a `foundation_prompt` fingerprint.

## Boundary probe (Phase 0, run once)

Add a class outside the custodian's namespace that writes `foundation-prompt.md`, run
`Grimoire.ArchTests`, confirm it **fails**, then delete the class and confirm it passes. A rule that has
never been seen to fail is not a guard.
