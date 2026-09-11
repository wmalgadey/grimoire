# Quickstart — Validating Feature 030

How to prove this feature works. The unusual part: **most of it is not provable by a test run**, and
saying so precisely is the point. The validation splits three ways — what CI proves, what a reviewer
proves by reading, and what only the operator loop proves over time.

## Prerequisites

- The repo at this feature's branch, with the four instruction documents edited.
- A live LLM provider credential, resolved the way `Grimoire.EvalRunner` already resolves it, for
  **step 3's re-capture and for steps 5 and 6** — those dispatch real ingest and lint runs against a
  scratch wiki, so they need a provider like any other run. Steps 1, 2 and 4 are hermetic.
- A build of the agent artifacts after editing the documents: instruction files are build-delivered
  (ADR-043), so an edited document does not reach a running agent until the artifacts are rebuilt.

## 1. The fast gates must stay green (hermetic, ~minutes)

```bash
./scripts/test-fast.sh
```

**Expected**: unchanged pass. This feature adds no code, so a failure here means something
unintended was touched — most likely an instruction file that a load-mechanism test reads for its
hash, or the ADR index.

## 2. Composition and load mechanism still hold (hermetic)

```bash
dotnet test backend/tests/Grimoire.IntegrationTests
```

**Expected**: unchanged pass, in particular the ADR-053 composition tests — both documents load
verbatim, in order, fail-closed, each with its own SHA-256 recorded.

**What this does *not* prove**: anything about what the documents say. That is deliberate (SC-002).
If you find yourself wanting to add an assertion here about the new frontmatter rows, stop — that is
the Principle V violation this feature exists to avoid re-introducing.

## 3. The staleness gate fires, then is cleared (this is the real cost)

Editing the instruction documents changes the fingerprint every replay recording is verified
against, so the gate must go **red first**:

```bash
dotnet run --project backend/tests/Grimoire.EvalRunner -- status
```

**Expected before re-capture**: **exit code 3**, listing the stale scenarios and the fingerprints
that changed. A *clean exit 0* here before re-capture would mean the fingerprints do not actually
cover the documents you edited — investigate rather than celebrate.

Use `status`, **not** `StalenessTests`. That test is a Fast-tier check of the staleness *mechanism*:
it copies the instruction files into a temporary fake repo root and introduces its own synthetic
drift, so it stays green no matter what you change in the real documents. `status` is the command
that evaluates the committed Ingest, Lint, Query and remediation manifests against the real files.

Re-capture each flagged scenario against a live provider, then replay:

```bash
# per scenario named in the staleness output
dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario <scenario-id>

dotnet test backend/tests/Grimoire.AgentEvals
```

**Expected after**: `status` exits 0 and the replay suite is green, with no scenario scoring against
a stale recording (SC-001, FR-012). **All four** ADR-033 replay classes are in scope, because every
one of them fingerprints the shared foundation document: `IngestReplayEvalTests`,
`LintReplayEvalTests`, `QueryReplayEvalTests` and `RemediationReVerificationEvalTests` — the last of
which also fingerprints the lint role document. Do not assume only ingest and lint are affected.

## 4. Read the four documents against each other (review, not a test)

Open [contracts/document-consistency.md](./contracts/document-consistency.md) and walk its authority
table. This is the whole of SC-006's verification, and it is a human step by design.

The four checks worth doing deliberately:

1. The integration-depth number matches **exactly** in the foundation and ingest documents — it is
   the one deliberate duplication.
2. Every per-role deviation is stated in *both* documents that carry it (FR-011).
3. No document justifies a rule by pointing at `docs/foundational/llm-wiki-*` (FR-007, FR-009).
4. No document acquired a rule the harness would have to enforce (FR-010a).

## 5. Exercise the changed judgment against a real wiki (manual, operator loop)

The lower-stakes criteria (SC-003 to SC-005) are verified by observing real behaviour, not by a
gate. Minimum useful pass:

```bash
# ingest a small source into a scratch wiki, then lint it
dotnet run --project backend/src/Grimoire.Hub -- submit-source --path <path-or-url>
dotnet run --project backend/src/Grimoire.Hub -- lint-run
```

Then look at the surfaces `plan.md` names, in this order:

| Look at | Expect | Criterion |
|---------|--------|-----------|
| The frontmatter of a page the ingest run created | `inbound_links` present; `last_reviewed` **absent** | SC-003, FR-002a |
| The `sources/` folder | a page for the source just ingested | SC-005 |
| Citation footnotes in the pages that run wrote | every `[[…]]` resolves to a page that exists | SC-005, FR-008 |
| The findings board on the submission page (`frontend/src/routes/+page.svelte`; `/board` only 308-redirects there) | review candidates are pages overdue for *review* | SC-004 |
| A page lint produced a finding about | `last_reviewed` now present | FR-002b |
| A page lint produced **no** finding about | `last_reviewed` **not advanced** — absent if it was never reviewed, otherwise still carrying its earlier date. Neither is a bug | FR-002e |

The last row is the one most likely to be misread as a bug. It is the intended behaviour (D5,
option B): a run that says nothing about a page does not count as a review of it, so the date stays
where it was. FR-002e is about the date not being *advanced*; it never removes an existing one.

## 6. Degraded operation against a foundation document that omits the fields

```bash
# point a scratch instance at a foundation document with no lifecycle rows, then lint
dotnet run --project backend/src/Grimoire.Hub -- lint-run
```

**Expected**: the run **completes** — it does not fail — and its findings report names each skipped
category and why (FR-010). The completion half is hermetically checkable and needs a **new**
integration test (no existing foundation-load test covers "readable but silent"; they cover absent,
unreadable and whitespace-only, which all fail closed by design). That the report explains itself is
agent behaviour, observed here and corrected through the instruction file if wrong.

**Expected NOT to happen**: any harness-level error about the foundation document's content. If the
harness noticed, FR-010a has been violated — and with it Constitution Principle V, which forbids the
harness from reinterpreting instruction content.

## What "done" means here

| Step | Proves | Kind |
|------|--------|------|
| 1–2 | nothing regressed; the load mechanism is intact | CI, hermetic |
| 3 | SC-001 / FR-012 — no scenario scores against a stale recording, across all four replay classes | CI, needs live capture once |
| 4 | SC-006 — the four documents agree | human review |
| 5 | SC-003 / SC-004 / SC-005 | operator loop, over time; needs a provider credential |
| 6 | FR-010 completion half hermetic; report half operator loop | mixed |

Steps 1–3 gate the merge. Steps 4–6 are how the feature is actually known to work, and the spec is
explicit that no eval threshold is added to convert them into gates.
