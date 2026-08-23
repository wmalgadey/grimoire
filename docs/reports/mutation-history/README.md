# docs/reports/mutation-history/

The durable half of mutation testing. `docs/reports/mutation/` holds the Stryker reports —
megabytes of generated HTML, gitignored, and deleted by the next run of the same target.
This directory holds what has to outlive them: a few kilobytes per run, tracked in git.

```
ledger.jsonl                      one line per (target, run) — score, counts, provenance, cost
snapshots/<target>/<run-id>.json  every mutant of that run: identity, status, covering tests
```

Written by [`scripts/mutation-history.py`](../../../scripts/mutation-history.py), which
`scripts/mutation-test.sh` calls for every target that completes (`MUTATION_HISTORY=0`
turns that off). Nothing here is a gate.

## Why this exists

Issue #181 measured the guardrail surface at **2.35 %**. A later re-run of the same script
against unchanged sources reported **1.38 %**, and the follow-up comment had to end:

> I cannot confirm that: `docs/reports/mutation/` is gitignored and this run overwrote the
> directory, so the original JSON is gone. […] the two scores are not a trend.

Two failures in one sentence. The first is storage: the evidence for a measurement was
deleted by the next measurement. The second is subtler and matters more — **the two
percentages were not comparable even in principle**, because the denominator had moved.
Stryker's Safe Mode drops *every* mutant in a method where one mutant failed to compile,
and dropped mutants leave the score entirely. 2.35 % of 383 scored mutants and 1.38 % of
715 are two different measurements of two different sets.

So this store does not treat the score as the unit of comparison. The unit is the
individual mutant.

## How a mutant is identified

```
<mutator name> | <sha256 of the replacement text, 8 chars> | <ordinal among identical ones in the file>
```

Not the line number: inserting a line at the top of a file would otherwise invalidate every
mutant below it and report the whole file as new. What is compared is *this equality flip,
in this file, the third one of its kind* — which survives edits elsewhere, and survives a
file moving between checkouts because paths are recorded relative to the Stryker project
root (`Grimoire.AgentRuntime/Guardrails/WriteJournal.cs`, never `/Volumes/Daten/…`).

`compare` therefore reports four things a score cannot:

- **killed → survived** — a real regression: an assertion that used to catch this is gone.
- **survived → killed** — a real win, attributable to the tests that were added.
- **only in A / only in B** — the code changed, or the mutate filter did.
- **denominator drift** — scored mutants and compile errors, side by side, with the
  warning that the percentages are not a trend. This is the check #181 could not finish.

`covered_by` is kept per mutant because it is the finding that made #181 worth filing:
survivors carrying 27 and 55 covering tests are not a coverage gap, they are tests that
execute the enforcement path and assert nothing about it. (Stryker.NET reports `coveredBy`
for mutants it did not kill and omits it for the ones it did, so the number is meaningful
for survivors.)

## Using it

```bash
# after a run — mutation-test.sh does this itself for each completed target
python3 scripts/mutation-history.py record docs/reports/mutation/domain
python3 scripts/mutation-history.py record docs/reports/mutation          # every target at once

python3 scripts/mutation-history.py list
python3 scripts/mutation-history.py list --target agent-runtime-guardrails

python3 scripts/mutation-history.py compare agent-runtime-guardrails            # last two runs
python3 scripts/mutation-history.py compare guardrails@84dfa3c guardrails@latest
python3 scripts/mutation-history.py compare domain --markdown                   # paste into a PR or issue
python3 scripts/mutation-history.py show agent-runtime-guardrails@latest
```

A selector is `[<target>@]<rev>`, where `<rev>` is `latest`, `latest~N`, a run-id prefix or
a commit prefix. `--commit <sha>` on `record` backfills an old report whose commit is known
but is not today's `HEAD`; it deliberately leaves branch and dirty-state empty rather than
recording today's tree as if it had been measured.

**What to commit.** The ledger line and the snapshot, whenever the run was worth having a
number for — with `--note` saying why, if it is not obvious. Not every experiment: the
store is append-only but it is also a git directory, and a snapshot is 8–60 KB (a full
`hub` run would be several hundred). Deleting an old snapshot while keeping its ledger line
is fine — `compare` says the snapshot is missing rather than failing. CI records nothing:
`.github/workflows/mutation.yml` posts its PR comment and keeps no history, because a
record per pull request is repository weight nobody reads.

## Reading the runtime column, and the strategy behind it

`list` prints wall clock and **seconds per scored mutant**, and that second number is the
one that decides whether a target can be measured sustainably at all. Mutation cost is
(mutants × time of the tests covering them), not (test time), so it is bounded by the suite
Stryker has to re-run — which for most targets here is the integration suite, with real
hosts and spawned agent processes.

Measured on this repository:

| Target | Mutants scored | Wall clock | Per mutant | Covering suite |
|---|---:|---:|---:|---|
| `domain` | 35 | ~1 min | ~1.8 s | 93 unit tests, in-process |
| `remediation-state-machine` | 38 | ~1 min | ~2.2 s | same |
| `agent-runtime-guardrails` | 383 | 24 min | ~3.7 s | the 911-test integration suite |
| `hub` | ~6600 | ~17 h (extrapolated) | ~9 s | same |

The consequence is a policy, not a preference:

- **Per-PR (CI):** only targets whose covering suite is in-process. Today that is the fast
  group, and it is the only thing `.github/workflows/mutation.yml` runs.
- **Per-session (recorded, compared):** targets under roughly half an hour — the guardrail
  surface. This is the band the history store is built for: run it, record it, change
  tests, run it again, `compare`.
- **Occasional (recorded, rarely repeated):** `hub` and the agent runtimes. An overnight
  job; the record exists so that the *next* one, months later, can still be compared
  against it instead of starting from nothing.

**This is also why #181 cannot be closed by writing assertions alone.** Its 264 non-string
survivors sit in code whose covering suite is the integration suite; each iteration of
"add an assertion, re-measure" costs 24 minutes, and the guardrail decisions being asserted
— stale-hash rejection, deny-by-default policy, lock acquisition — do not need a real host
or a spawned process to be exercised. That is exactly the separation issue #180 proposes:
tier membership by assembly, with the guardrail decision tests in a DevLoop assembly that
builds no host and starts no process. Once they live there, `agent-runtime-guardrails`
becomes a fast-tier target: minutes, per PR, per iteration.

So the two issues meet here. **#180 moves the tests and changes the cost per mutant; #181
adds the assertions and changes the score.** They are separately visible in this store —
the first as a drop in the runtime column with the mutant identities unchanged, the second
as survived → killed rows with the runtime unchanged — and that is the point of recording
both numbers against every run. Neither of them is work this directory does; it is the
instrument that will show whether they worked.
