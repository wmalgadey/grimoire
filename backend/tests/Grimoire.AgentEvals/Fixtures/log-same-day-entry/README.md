# Fixture: `log-same-day-entry`

Seeds `wiki/log.md` with **one entry dated the same calendar day as the scenario's capture
run**, so `log-no-day-grouping` (SC-007) samples the behaviour the operator actually asked
about: an action logged on a date that already has an entry must produce its own complete
entry with its own date heading, not get merged into the existing day's section.

## ⚠️ Re-seed the date whenever you re-record

The seeded date is **hard-coded** to `2026-08-17`, the date of the scenario's capture run.

It has to be. `EvalWorkspace` copies this directory verbatim per sample and replayed model
turns are frozen at capture time, so a fixture cannot compute "today" — and if it could, the
recorded agent output would still carry the capture date, and the two would drift apart on
every replay.

**If you re-record `log-no-day-grouping`, re-seed this file's date in the same change:**

```bash
dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario log-no-day-grouping
```

The capture run's date must match the `## [YYYY-MM-DD]` heading above. If it does not, the
scenario silently degrades into the generic "prepend an entry" case — it still passes, but it
stops testing day-grouping at all, which is the one thing it exists to test. Nothing detects
that for you: the recording will be internally consistent and the score will look fine.

The fixture directory is part of the staleness fingerprint, so editing this date correctly
invalidates the recording and forces the re-capture — which is the intended coupling, not an
inconvenience to work around.

## Contents

- `wiki/index.md` — the same empty catalog `empty-topic` uses.
- `wiki/log.md` — one seeded entry, dated as above.
