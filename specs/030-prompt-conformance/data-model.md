# Phase 1 Data Model — Feature 030, Prompt Conformance

The "data model" for this feature is the wiki page's frontmatter block and the confidence
convention applied to it. Neither is a database schema: both are conventions stated in instruction
documents and honoured by agents (Principle V). Nothing here is validated by backend code, and
nothing here may be.

---

## Entity: Wiki page frontmatter

Every wiki page except `index.md` and `log.md` carries this block. Fields are grouped by what they
are *for*, which is the grouping the foundation document should also use.

| Field | Group | Required | Written by | Changed by this feature |
|-------|-------|----------|-----------|--------------------------|
| `type` | Identity | Yes | creating run | No |
| `title` | Identity | Yes | creating run | No |
| `description` | Identity | Yes | creating run, refreshed when scope changes | No |
| `timestamp` | Provenance | Yes | every run that writes the page | No |
| `resource` | Provenance | When a canonical URI exists | creating run | No — but FR-008 now requires the page type that most needs it |
| `supersedes` / `superseded_by` | Provenance | On supersession only | the superseding run | No |
| `tags` | Classification | Yes, ≥ 2 | creating run; lint *proposes* corrections | No |
| `confidence` | Assessment | Yes | creating run; lint *proposes* corrections | Value semantics change (see convention below) |
| `confidence_reason` | Assessment | Yes | alongside `confidence` | No |
| **`inbound_links`** | **Lifecycle** | **Yes** | **creating run (provisional); lint corrects** | **NEW — FR-002a** |
| **`last_reviewed`** | **Lifecycle** | **No — present only once reviewed** | **the reviewing run only** | **NEW — FR-002b** |

### Field: `inbound_links`

- **Canonical definition**: the number of `[[wikilink]]` occurrences naming this page across all
  *other* files, including `index.md` and `log.md`. Self-references never count; repeats from the
  same file each count. This definition lives in the foundation document once, and lint's counting
  procedure refers to it rather than restating it.
- **At creation** the value is the creating run's best observation — for ingest, the links that run
  itself wrote; for query, the links its own writes produce. This value is **explicitly
  provisional**: a creating run cannot see the whole link graph, so a pre-existing incoming link, or
  one added by another file in the same run, can make it wrong immediately.
- **Correction** is lint's, and FR-002a states that the provisional value does not forbid lint from
  recomputing. FR-004's saving is that lint no longer has to *create* the field wiki-wide — not that
  it can skip reading.
- **Absent** is distinguishable from **zero** and means "no run has written this yet", which is what
  lint's existing refresh rule already keys on.

### Field: `last_reviewed`

- **Presence is the semantics.** The field exists on a page if and only if that page has been
  reviewed. This is what makes lint's existing fallback to `timestamp` correct rather than a
  workaround: a page that has never been reviewed genuinely *is* overdue measured from its ingest
  date.
- **Sole writer**: the reviewing run (lint). Ingest never writes it, on create or on update.
- **Qualifying act** (D5, option B): the run produced a substantive finding or a remediation
  *proposal* about this page.
- **Two exclusions that are part of the field's definition, not footnotes**:
  - The review-candidate listing itself does **not** qualify (FR-002d). It is the output of the
    check this field feeds, so counting it would make the check self-clearing.
  - A page about which a run produced no finding is **not** stamped (FR-002e). Intended, not a gap:
    the candidate list then means "low-confidence pages that nothing has been said about".

### State transitions

```
                    creating run (ingest or query)
                              │
                              ▼
        ┌──────────────────────────────────────────┐
        │ inbound_links: <provisional count>       │
        │ last_reviewed: ABSENT                    │   ← lint's review window measures
        └──────────────────────────────────────────┘      from `timestamp`, correctly
                              │
              lint run produces a finding or
              remediation proposal about it
                              │
                              ▼
        ┌──────────────────────────────────────────┐
        │ inbound_links: <corrected count>         │
        │ last_reviewed: <date>                    │   ← review window now measures
        └──────────────────────────────────────────┘      from the review
                              │
              lint run produces no finding about it
                              │
                              ▼
                    unchanged — deliberately
```

There is no transition that removes either field.

---

## Entity: Confidence convention

Two scales, one threshold table. Both are stated in documents, never computed by code — and, after the 2026-09-10 clarification, never asserted by a requirement either: the convention is a judgment aid the agent applies, not a contract.

### Shared signal set — every agent

Applies to ingest, query and lint alike. Range `−3 … +2`.

| Signal | Points |
|--------|--------|
| Three or more independent sources | +1 |
| Source is a book or official documentation | +1 |
| Source is a social-media or blog post | −1 |
| Page carries an explicit contradiction marker (⚠️) | −1 |
| Source older than 18 months on a fast-moving topic | −1 |

### Lint-only extension

Lint alone can observe the link graph, which is why these are not in the shared set. Range
`−4 … +3`.

| Signal | Points |
|--------|--------|
| `inbound_links` ≥ 3 | +1 |
| `inbound_links` = 0 (orphan) | −1 |

Lint applies these to the **corrected** count, never to a stale value on disk.

### Thresholds — identical for both scales

| Band | Total |
|------|-------|
| `high` | ≥ 1 |
| `medium` | −1 … 0 |
| `low` | ≤ −2 |

These numbers are a **recommendation with recorded reasoning**, not a verified property: FR-005
states no MUST-level requirement about the convention's shape, so nothing checks how the bands come
out. [research.md](./research.md) R1 works through how each band is reached on both ranges — that is
the argument for choosing these cut points over the current `high ≥ 2`, which demands a perfect
score, and it is why the numbers are what they are. It is not a test they pass.

That the two scales share one threshold table is itself a decision and must be stated in the
documents, alongside the consequence FR-006 requires: **a page's score can legitimately change when
lint re-scores it**, and both documents say so.

### Query's synthesis pages

FR-006a requires query's treatment to stop being an undeclared third reading. Two outcomes were
admissible; **this design picks the first**, because leaving the choice open to the implementation
layer would let the four documents stay inconsistent — which is the defect the feature exists to
remove.

**Decision: a synthesis page is scored with the shared formula, applied to the provenance of the
wiki pages it draws on.** No narrative carve-out, no third reading.

The mapping is worth stating, because the shared signals are phrased for *sources* while a synthesis
draws on *pages* — this is vocabulary the agent needs in order to apply the formula at all, not a
computation it is required to perform:

| Shared signal | How a synthesis page evaluates it |
|---------------|-----------------------------------|
| Three or more independent sources | Count the distinct pages the synthesis draws on, not their upstream sources — query cannot see those. |
| Source is a book or official documentation | Inherited: it fires if a drawn-on page's own provenance records one. |
| Source is a social-media or blog post | Inherited the same way. |
| Explicit contradiction marker (⚠️) | Fires if the synthesis itself records a contradiction, or if any page it draws on carries one. |
| Source older than 18 months on a fast-moving topic | Inherited from the drawn-on pages' provenance. |

**Where the inherited signals disagree, the weighting is the agent's judgment — the document
prescribes no aggregation rule.** An earlier draft of this design mandated "the synthesis takes the
weakest reading"; that was withdrawn on 2026-09-10, because a fixed aggregation rule is exactly the
deterministic prescription over a judgment aid that FR-005's clarification rejects. Confidence
scoring is a means for the agent to exercise judgment, not a formula the agent executes.

What remains a decision is only the *choice between the two treatments* FR-006a offers: the shared
formula rather than a narrative carve-out. That much the document must state visibly, so query stops
being an undeclared third reading of a convention the other two agents share.

---

## Entity: Source-summary page

Not a new entity — the `Source summary` page type, its `sources/<slug>.md` location and its
`resource` rule already exist in the foundation document. What changes is that FR-008 makes
producing one a requirement of an ingest run rather than an available option.

| Property | Value |
|----------|-------|
| `type` | `Source summary` |
| Location | `sources/<slug>.md` |
| `resource` | The original source's canonical URI **when one exists**; a source with no URI (pasted text, uploaded file) still gets a page, identified by other means, with no fabricated value |
| Referenced by | Citation footnotes in the pages the run wrote, e.g. `[^2]: [[sources/some-source]]` |
| Uniqueness | One page per source. A second ingest of the same source updates or supersedes it under the existing supersession rules; it never creates a second page under a different slug. |

---

## What is deliberately *not* modelled here

- **No backend type, table, DTO or validator.** Adding one would move wiki-content judgment into
  deterministic code, which Principle V forbids and which is the failure this whole feature exists
  to reverse.
- **No frontmatter schema file.** The frontmatter standard is prose in an instruction document by
  design; a machine-readable schema would become a second source of truth that could drift from the
  document — the exact class of bug issues #109–#111 report.
