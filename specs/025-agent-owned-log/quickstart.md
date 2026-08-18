# Quickstart: Validating the Agent-Owned, Newest-First Activity Log

**Feature**: 025-agent-owned-log | **Date**: 2026-08-17

Runnable validation for this feature. Each scenario names what it proves, which success
criterion it covers, and what you should see. Details live in
[contracts/activity-log-write-contract.md](contracts/activity-log-write-contract.md) and
[data-model.md](data-model.md) — this file does not repeat them.

## Prerequisites

- .NET SDK matching `backend/Directory.Build.props`
- A built solution: `dotnet build backend/Grimoire.slnx --configuration Release`
- **Scenarios 1–5 need no credentials, no network, and no recordings.**
- Scenario 6 (evaluation tier) replays committed recordings — no live provider call, but it
  is the slow, opt-in tier.
- Scenario 7 (manual end-to-end) needs a running Hub and `ANTHROPIC_AUTH_TOKEN` in `.env`.

---

## Scenario 1 — The guardrail enforces newest-first

**Proves**: SC-001, SC-004 — a conforming prepend is allowed; anything that modifies,
reorders, or removes existing content is denied with a recorded reason. **Tier**: Integration.

```bash
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release \
  --filter "FullyQualifiedName~LogEntryFormatEnforcement"
```

**Expect**: green. Specifically, cases asserting that

- a write whose proposed content **ends with** the current content and opens with a
  conforming heading + paragraph is **allowed**, and the committed file has the new entry
  first with the prior bytes as an exact suffix;
- the old append shape (current content first, new entry at the end) is now **denied**
  `log_entry_not_prepended`, with the file left unchanged;
- an edit, re-sort, or removal of any existing entry is denied the same way.

**Red-first check**: before the guard is inverted, the "prepend is allowed" case fails with
`log_entry_not_appended`. That failure is the point — it is the guardrail that made the
operator's request impossible.

---

## Scenario 2 — Missing, empty, and duplicate-heading logs

**Proves**: SC-003, SC-004, FR-009, FR-010. **Tier**: Integration.

Same command as Scenario 1. **Expect** cases covering:

- no file on disk → the first write is allowed and creates it;
- a zero-byte file → same;
- two successive allowed prepends whose headings are byte-identical → both entries present,
  both matching `^## \[\d{4}-\d{2}-\d{2}\] .+ \| .+$`, neither merged;
- a prepend of whitespace only → denied `log_entry_malformed_heading`;
- a prepend of a heading with no paragraph → denied `log_entry_missing_paragraph`.

---

## Scenario 3 — No harness component can write the log (structural)

**Proves**: SC-002 via Boundary Rule BR-1. **Tier**: Fast.

```bash
./scripts/test-fast.sh
# or just the rule:
dotnet test backend/tests/Grimoire.ArchTests --configuration Release \
  --filter "FullyQualifiedName~GuardedWriteBoundaryRule"
```

**Expect**: green for all three agents (Ingest, Query, Lint) with
`Grimoire.AgentRuntime.WikiLog` no longer on the allow-list.

**Red/Green probe** (required by Constitution Principle III — the tightened rule is a
different rule from the one probed in feature 014, so it must be re-probed):

1. Add a temporary class in `Grimoire.AgentRuntime.WikiLog` calling
   `File.AppendAllTextAsync`.
2. Re-run the command — it MUST fail, naming that type and the write call.
3. Delete the class and re-run — green.

Record the probe's outcome in the task; a rule that was never observed failing is not a
proven guard.

---

## Scenario 4 — Failed and no-write runs leave the log untouched

**Proves**: SC-002, SC-008. **Tier**: Integration.

```bash
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release \
  --filter "FullyQualifiedName~RestartReconcilerActivityLog|FullyQualifiedName~IngestFailureAndReconciliation|FullyQualifiedName~QuerySynthesisWrite"
```

**Expect**:

- restart reconciliation marks the task `failed` in its artifact and records the transition
  in the status history, while `log.md` is **byte-for-byte unchanged** (FSI-2);
- a failed Ingest run leaves `log.md` unchanged, and its failure, stage, and task id are all
  discoverable from the task artifact and operational state **without opening the wiki**;
- a Query turn that answers without writing a page leaves `log.md` unchanged, and the turn is
  fully recorded in its conversation record.

Grep is a valid extra assertion here: no test-produced `log.md` should contain the string
`harness backstop` anywhere.

---

## Scenario 5 — The replacement operational signal

**Proves**: SC-009, FR-012a. **Tier**: Integration.

```bash
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release \
  --filter "FullyQualifiedName~WikiLogCoverageObservability"
```

**Expect**, collected through the production telemetry registration (not a hand-attached
listener):

- a run that writes a page but not the log emits `wiki.log.change_not_logged` at **WARN**
  with `type`, `task_id_or_run_id`, and `wiki_content_writes`, increments
  `wiki.log.unlogged_change_total` labelled `type`, and produces a
  `wiki_log.coverage_check` span with the event span as its **child**, sharing
  `task_id_or_run_id`;
- **and `log.md` is absent or unchanged** — the signal never writes to the wiki;
- the control case (a run that writes both a page and the log) emits **no**
  `wiki.log.change_not_logged`, and its `wiki_log.coverage_check` span reports
  `outcome=logged`.

Retired signals must be gone: no `wiki.log.backstop_appended`, no
`wiki.log.backstop_appended_total`, no `wiki_log.backstop_append`.

---

## Scenario 6 — Agent behaviour (evaluation tier)

**Proves**: SC-005, SC-006, SC-007 at their ≥90% thresholds. **Tier**: SlowEval, opt-in.

```bash
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --filter "Tier=SlowEval"
```

**Expect** the three new scenarios to score ≥ 0.90 over their sampled runs:

| Scenario | What it samples |
| --- | --- |
| `log-newest-first-placement` | Ingest runs that changed the wiki add exactly one new entry, at the top, over an unchanged suffix |
| `log-changes-only` | Query turns that write no page leave the log byte-for-byte unchanged |
| `log-no-day-grouping` | An action on a date that already has an entry produces a separate complete entry, leaving the earlier one's section unchanged |

The existing `log-paragraph-specificity` scenario must also stay green — it covers the
"accurately describes what changed" half of SC-005 and is deliberately not modified.

**Re-recording caveat**: `log-no-day-grouping`'s fixture hard-codes its seeded entry's date to
the capture run's date so replay is deterministic. If you re-record that scenario, re-seed the
fixture's date in the same change, or the scenario silently degrades into the generic case.
This is stated in the fixture's own README.

---

## Scenario 7 — End-to-end, by eye

**Proves**: the operator-visible outcome from issue #89. **Manual.**

1. Start the Hub against a scratch content root.
2. Submit two ingests that each change the wiki, on the same day.
3. Open `log.md` at the content root.

**Expect**: the second run's entry sits at the very top; the first run's entry is present and
unmodified below it; each has its own `## [YYYY-MM-DD] …` heading (no merged day section);
and no line anywhere reads like harness bookkeeping.

4. Ask a question the wiki can already answer (a routine lookup that writes nothing).

**Expect**: `log.md` unchanged.

5. Force a run to fail (e.g. stop the Hub mid-run and restart it so reconciliation kicks in).

**Expect**: `log.md` unchanged; the failure visible in the task artifact and in the Hub's
operational state.

---

## Full merge gate

```bash
dotnet build backend/Grimoire.slnx --configuration Release
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release
```

The frontend is untouched by this feature; its suites should be unaffected.
