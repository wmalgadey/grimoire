# SC-014 read-shape baseline — 026-guarded-tool-surface

**Criterion**: SC-014 is *withdrawn as a gating criterion* (spec.md, clarified 2026-08-22).
The numbers below are a **measurement**, not a gate: they record the effect the feature is
expected to have, and nothing fails if the effect is smaller than expected.

**Tasks**: T004 records the "before" half; T069 records the "after" half and the comparison.

## Method

The measurement is derived from the checked-in recorded-replay eval samples rather than
from a fresh live run, for three reasons:

1. The recordings *are* live Lint survey runs — they were captured against a real model.
2. Deriving both halves the same way makes the comparison like-for-like. A fresh live run
   would introduce sampling variance into the "before" number that the "after" number,
   taken from recordings, would not share.
3. It is reproducible: anyone can re-derive these numbers from the repository at the named
   commit, with no credentials.

For each sample, "content tokens read" is the sum, over every `read_file` call the sample
records, of the tokens of the page content that call returned — resolved against the
scenario's fixture on disk, honouring `offset`/`limit`/`frontmatter_only` where present, and
counting reads nested inside a `batch` call as the individual reads they are. Tokens are
approximated as `characters / 4`, applied identically to both halves; the ratio is the
quantity of interest, not the absolute count.

`shapes` counts `read_file` invocations by the `shape` label of
`wiki.read.invocations_total` (ADR-030 R3): `full` | `range` | `frontmatter`.

## Before — pre-feature state

Measured at commit `301242b` (the tip of layer 08), **before** T009 (`policy.json` v2),
T012 (`LintToolRegistry` switch-flip) and T065 (`system-prompt.md`) landed. At that commit
the Lint agent is behaviourally pre-feature: `LintToolRegistry.Default` declares only
`list_files`/`read_file`/`write_file`, and `read_file` is advertised with the unranged
`ReadFileDefinition` schema — so no ranged or frontmatter read is reachable, and the
all-`full` shape counts below are a property of the build, not an accident of sampling.

| Scenario | Fixture | n | median | mean | min | max | shapes (full/range/fm) |
|---|---|---:|---:|---:|---:|---:|---|
| `lint-defects-found` | `lint-seeded-defects` | 10 | 3378 | 3407 | 2953 | 4900 | 210 / 0 / 0 |
| `lint-genuine-findings` | `lint-seeded-defects` | 10 | 2953 | 3268 | 1586 | 5130 | 200 / 0 / 0 |
| `lint-metadata-proposals` | `lint-seeded-defects` | 10 | 3302 | 3687 | 1586 | 6847 | 226 / 0 / 0 |
| `lint-remediation-proposals` | `lint-seeded-defects` | 10 | 2953 | 3471 | 2953 | 4900 | 213 / 0 / 0 |
| `lint-inbound-links-refreshed` | `lint-inbound-links-fixture` | 10 | 782 | 782 | 782 | 782 | 80 / 0 / 0 |

**Headline before-number** (the four `lint-seeded-defects` survey scenarios, 40 samples
pooled): median **3012.5** content tokens read per Lint survey run; 849 of 849 reads
(100%) whole-page.

`lint-inbound-links-refreshed` is listed for completeness but is excluded from the headline:
its fixture is three pages, every sample reads all of them, and the variance is zero — it
cannot show narrowing because there is nothing to narrow.

## After — post-feature state

Measured from the recordings captured by T068, on the same commit as the shipped
`policy.json` v2, `LintToolRegistry`, and `system-prompt.md`. Same method, same tokenizer
approximation, same script.

| Scenario | Fixture | n | median | mean | min | max | shapes (full/range/fm) |
|---|---|---:|---:|---:|---:|---:|---|
| `lint-defects-found` | `lint-seeded-defects` | 10 | 2959 | 3066 | 1808 | 3541 | 176 / 2 / 29 |
| `lint-genuine-findings` | `lint-seeded-defects` | 10 | 3149 | 3206 | 2430 | 3818 | 184 / 0 / 31 |
| `lint-metadata-proposals` | `lint-seeded-defects` | 10 | 3094 | 3379 | 1589 | 5565 | 187 / 14 / 28 |
| `lint-remediation-proposals` | `lint-seeded-defects` | 10 | 3611 | 3410 | 2959 | 3818 | 195 / 0 / 28 |
| `lint-inbound-links-refreshed` | `lint-inbound-links-fixture` | 10 | 786 | 684 | 446 | 786 | 71 / 0 / 0 |
| `lint-at-scale-survey` | `lint-at-scale` | 10 | 7209 | 7482 | 3685 | 10634 | 186 / 0 / 285 |

## The comparison

**On the small fixture, the expected effect does not appear — and should not.**

Pooled across the same four `lint-seeded-defects` survey scenarios:

| | before | after |
|---|---:|---:|
| median content tokens read | 3012.5 | 3149.0 |
| read shapes (full / range / frontmatter) | 849 / 0 / 0 | 742 / 16 / 116 |

Content read went **up** by about 4.5%, not down by 50%. This is not a failure of the
feature and it is not noise being explained away: `lint-seeded-defects` is nine pages
totalling roughly 1 600 content tokens. There is nothing to narrow. Reading every page is
the correct survey strategy at that size, the agent does it, and the ranged and
frontmatter-only reads that do appear (16 and 116 of 874) are the agent sampling metadata
before deciding — a small extra cost, paid for judgment it did not previously exercise.

A criterion phrased as "median content tokens read drops ≥ 50% against the pre-feature
baseline", evaluated here, would read as a miss. It would be measuring a wiki the feature
was never aimed at. SC-014 was withdrawn as a gating criterion for a different and better
reason (it is a measurement, not a judgment), but this is the concrete shape of why gating
on it would have been wrong.

**On a wiki past the context guard, the effect is the whole point.**

`lint-at-scale` is 69 pages and **50 895** content tokens. Before this feature, the only
read shape that existed was the whole file: `read_file` took a bare `path`, with no
`offset`, `limit`, or `frontmatter_only`. Covering that wiki therefore cost its full
50 895 tokens, and there was no cheaper option available at any price.

| | whole-wiki read (the pre-feature surface's only option) | measured after |
|---|---:|---:|
| content tokens read | 50 895 | **7 209** (median) |
| reduction | — | **≈ 86%** |
| read shapes (full / range / frontmatter) | every page, in full | 186 / 0 / 285 |

Frontmatter-only reads outnumber whole-page reads roughly 3:2. That is the mechanism
working exactly as ADR-030 R3 and the `Choosing how to read` instructions describe: sweep
metadata cheaply across the whole wiki, then spend a full read only where a page has earned
one. Ranged reads go unused on this fixture, which is fair — its pages are short enough
that a slice saves little, and the agent choosing not to use a capability where it would not
help is the correct judgment rather than a gap.

**Summary.** The effect SC-014 predicted holds decisively where the wiki exceeds the
context guard (≈ 86% less content read) and is absent, slightly negative, on a wiki small
enough to read whole. Both halves are what the design implies; recording only the pooled
small-fixture number, or only the at-scale one, would each have misrepresented the feature.
