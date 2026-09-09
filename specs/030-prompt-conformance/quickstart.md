# Quickstart — Validating Feature 030

How to prove this feature works. The unusual part: **most of it is not provable by a test run**, and
saying so precisely is the point. The validation splits three ways — what CI proves, what a reviewer
proves by reading, and what only the operator loop proves over time.

## Prerequisites

- The repo at this feature's branch, with the four instruction documents edited.
- For the re-capture step only: a live LLM provider credential, resolved the way
  `Grimoire.EvalRunner` already resolves it. Every other step is hermetic.

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
dotnet test backend/tests/Grimoire.AgentEvals --filter FullyQualifiedName~StalenessTests
```

**Expected before re-capture**: failures naming the stale scenarios and printing the refresh
command. A *pass* here before re-capture would mean the fingerprint does not actually cover the
documents you edited — investigate rather than celebrate.

Re-capture each flagged scenario against a live provider, then replay:

```bash
# per scenario named in the staleness output
dotnet run --project backend/tests/Grimoire.EvalRunner -- capture --scenario <scenario-id>

dotnet test backend/tests/Grimoire.AgentEvals
```

**Expected after**: green, with no scenario scoring against a stale recording (SC-001). Affected
classes are `IngestReplayEvalTests` and `LintReplayEvalTests` (ADR-033).

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
dotnet run --project backend/src/Grimoire.Hub -- ingest --source <path-or-url>
dotnet run --project backend/src/Grimoire.Hub -- lint
```

Then look at the surfaces `plan.md` names, in this order:

| Look at | Expect | Criterion |
|---------|--------|-----------|
| The frontmatter of a page the ingest run created | `inbound_links` present; `last_reviewed` **absent** | SC-003, FR-002a |
| The `sources/` folder | a page for the source just ingested | SC-005 |
| Citation footnotes in the pages that run wrote | every `[[…]]` resolves to a page that exists | SC-005, FR-008 |
| The findings board (`frontend/src/routes/board`) | review candidates are pages overdue for *review* | SC-004 |
| A page lint produced a finding about | `last_reviewed` now present | FR-002b |
| A page lint produced **no** finding about | `last_reviewed` still absent — this is correct | FR-002e |

The last row is the one most likely to be misread as a bug. It is the intended behaviour (D5,
option B): the candidate list means "low-confidence pages that nothing has been said about".

## 6. Degraded operation against a foundation document that omits the fields

```bash
# point a scratch instance at a foundation document with no lifecycle rows, then lint
dotnet run --project backend/src/Grimoire.Hub -- lint
```

**Expected**: the run **completes** — it does not fail — and its findings report names each skipped
category and why (FR-010). The completion half is hermetically checkable and belongs in the test
suite; that the report explains itself is agent behaviour, observed here and corrected through the
instruction file if wrong.

**Expected NOT to happen**: any harness-level error about the foundation document's content. If the
harness noticed, FR-010a has been violated (ADR-055 R2).

## What "done" means here

| Step | Proves | Kind |
|------|--------|------|
| 1–2 | nothing regressed; the load mechanism is intact | CI, hermetic |
| 3 | SC-001 — no scenario scores against a stale recording | CI, needs live capture once |
| 4 | SC-006 — the four documents agree | human review |
| 5 | SC-003 / SC-004 / SC-005 | operator loop, over time |
| 6 | FR-010 completion half hermetic; report half operator loop | mixed |

Steps 1–3 gate the merge. Steps 4–6 are how the feature is actually known to work, and the spec is
explicit that no eval threshold is added to convert them into gates.
