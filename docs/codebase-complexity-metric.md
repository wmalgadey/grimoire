# Codebase Complexity Metric

**Role**: Source material documenting the methodology behind the two README badges; not
binding for SDD (see Document Map in `CLAUDE.md`).
**Reader**: Contributors and newcomers who want to know what the README's "code
complexity" and "est. time to understand" badges mean, how they're computed, and how to
regenerate them.
**Scope**: `backend/src` (C#) and `frontend/src` (TypeScript/JavaScript/Svelte) — the same
two directories `.github/workflows/complexity.yml` already scans for the PR complexity
gate. Tests, specs, docs, and agent instruction files are excluded, same as that workflow.

## What the badges show

README.md displays two [shields.io endpoint badges](https://shields.io/badges/endpoint-badge),
generated from static JSON files at `docs/metrics/complexity-badge.json` and
`docs/metrics/understanding-time-badge.json`:

- **code complexity** — the average cyclomatic complexity (CCN) per function, with a
  Low/Moderate/High/Very High rating.
- **est. time to understand** — a heuristic estimate of how long a single engineer,
  unfamiliar with the codebase, would need to read through it once at a careful,
  comprehension-level pace.

Both are computed by `scripts/ci/compute-codebase-metrics` from a
[lizard](https://github.com/terryyin/lizard) CSV export — the same tool and CSV format
already used by the PR complexity report (`scripts/ci/format-complexity-report`) and the
complexity regression gate (`scripts/ci/check-complexity-delta`).

## Why cyclomatic complexity, not the Halstead/MI analysis

`docs/code-complexity-analysis.md` already computes a fuller picture per file — Halstead
volume/difficulty and a Maintainability Index (MI) — including Halstead's own "time to
program" formula, `T = Effort / 18` seconds. That formula was deliberately **not** reused
for the "time to understand" badge: it's calibrated for original, from-scratch authoring
effort (design, writing, debugging), and applying it to this codebase yields a headline
figure in the hundreds of working days — plausible as an *effort-to-build-from-scratch*
estimate, wildly overstated as a *reading* estimate. Reading unfamiliar code is a much
cheaper activity than writing it, so a comprehension-time estimate needs a model of
reading speed, not authoring effort. See "Method 2" below.

Average CCN was chosen as the complexity driver over the fuller MI score for a narrower
reason: it's the same metric `.github/workflows/complexity.yml` already computes and
gates PRs on, so the README badge, the PR report, and the regression gate all read off one
consistent number instead of introducing a second, differently-tuned complexity scale.

## Method 1 — complexity rating

`avg CCN` is the mean cyclomatic complexity across every function lizard finds in scope.
It's rated against the same bands already established in this repository:

| avg CCN | Rating | Matches |
| --- | --- | --- |
| ≤ 5 | Low | "Good" band, `docs/code-complexity-analysis.md` |
| 5–10 | Moderate | "Acceptable" band, `docs/code-complexity-analysis.md` |
| 10–15 | High | "Refactoring candidate" band, `docs/code-complexity-analysis.md` |
| > 15 | Very High | `CCN_THRESHOLD` in `.github/workflows/complexity.yml` |

Cyclomatic complexity itself is McCabe's 1976 metric: the number of linearly independent
paths through a function's control-flow graph. It's the most widely cited single-number
complexity metric in software engineering literature and tooling (NIST Special
Publication 500-235; SonarQube, Visual Studio, and lizard all report it).

## Method 2 — estimated time to understand

There is no single agreed-upon formula for "time to understand a codebase," so this is
an explicit, documented heuristic rather than a scientific measurement:

```
hours = (total_NLOC / 400) × complexity_factor
complexity_factor = clamp(avg_CCN / 5, 1.0, 3.0)
```

- **400 NLOC/hour** is the base reading rate, in the middle of the range reported for
  careful code reading. Cisco's code-review study (summarized in SmartBear's *Best Kept
  Secrets of Peer Code Review*) found effective inspection rates cluster around
  300–500 lines/hour for application code before defect-detection quality drops off;
  400 is the midpoint. This is a citation for the *order of magnitude*, not a claim that
  Grimoire's code reads exactly like the code in that study.
- **complexity_factor** scales the base rate up as the code gets harder to follow: it
  stays at 1× up to the "Low" ceiling (avg CCN 5) and rises linearly to a 3× cap at avg
  CCN 15 — the same threshold the CI gate treats as "too complex." A codebase sitting
  right at the gate's own complexity limit is modeled as taking three times as long to
  read as one comfortably below it.
- The resulting hours are converted to working days (8h) and, past 10 working days, to
  five-day weeks, rounded to the nearest whole unit.

Read this as an order-of-magnitude signal — "does this codebase look like a day's read or
a month's read" — not a commitment.

## Regenerating the badges

Run after a significant change to `backend/src` or `frontend/src`, from the repo root:

```bash
pip install lizard
python3 -m lizard backend/src frontend/src -l csharp -l typescript -l javascript \
  -C 15 -i -1 --csv > /tmp/codebase-complexity.csv
scripts/ci/compute-codebase-metrics /tmp/codebase-complexity.csv --out-dir docs/metrics
```

Commit the two updated files under `docs/metrics/`. The badges are not regenerated
automatically by CI — they're refreshed by whoever touches this area next, same as
`docs/code-complexity-analysis.md`.
