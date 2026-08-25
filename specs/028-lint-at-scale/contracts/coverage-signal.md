# Contract: The Wiki-Coverage Signal

Extends two existing contracts additively — the Lint agent process's NDJSON terminal event,
and the `grimoire-findings/1` Findings Report format (`specs/013-lint-agent/contracts/
findings-report-format.md`). Neither format bumps version: the NDJSON terminal event is an
internal process-boundary protocol with no external consumers to break, and the Findings
Report's own contract explicitly reserves its bookkeeping block as "an open YAML mapping;
future optional keys... can be added without restructuring" (spec 013's forward-compatibility
clause) — this is exactly that case.

## NDJSON terminal event (`Grimoire.LintAgent` → Hub)

`RunCompletionMetadata` (`backend/src/Grimoire.LintAgent/RunEvents/RunEventEmitter.cs:13-38`)
gains one field:

```jsonc
{
  // ...existing RunCompletionMetadata fields (TurnsUsed, DeniedActions, InboundLinksRefreshed, ...)
  "wiki_coverage": {
    "pages_total": 633,
    "pages_considered": 611,
    "status": "partial"          // "complete" | "partial"
  }
}
```

- `pages_total`: filesystem page count at run start.
- `pages_considered`: `|ConsideredPaths|` at run completion (data-model.md).
- `status`: `"complete"` iff `pages_considered == pages_total`, else `"partial"`.

This is computed by the agent-process harness code (`GuardedToolExecutor` +
`LintIntentHandler`), not by the agent's own narrative — the agent's final message plays no
role in producing this block (FR-004).

## Findings Report bookkeeping block (`grimoire-findings/1`)

One new YAML mapping, sibling to the existing `inbound_links_refreshed` key
(`FindingsReportFormat.cs:84`):

```yaml
---
run_id: 2026-08-24-lint-a1b2c3d4
record_format: grimoire-findings/1
triggered_at: 2026-08-24T10:00:00.0000000+00:00
completed_at: 2026-08-24T10:04:12.0000000+00:00
outcome_state: completed
failure_reason: null
partial: false                    # existing field — run-outcome axis (crashed vs not)
instruction_file:
  path: "agents/lint/system-prompt.md"
  sha256: "7f2a…"
denied_actions: []
inbound_links_refreshed: 42
wiki_coverage:                    # new — wiki-coverage axis (orthogonal to `partial`)
  pages_total: 633
  pages_considered: 611
  status: partial
-->
```

**`partial` and `wiki_coverage.status` are independent.** A run can have `partial: false`
(finished cleanly) and `wiki_coverage.status: partial` (chose or was budgeted to examine less
than the whole wiki) at the same time — this is the expected common case once Direction A's
narrowing is working as intended, not an error state. The reverse combination (`partial:
true`, `wiki_coverage.status: complete`) is also possible in principle (a run that examined
every page and then failed for an unrelated reason before finishing its writeup) and MUST NOT
be treated as contradictory by any consumer.

## Verification approach (no new parser)

Per the existing Findings Report contract, no production code parses this format back into
structured data, and this feature does not change that. SC-002 ("100% of completed Lint runs
carry a coverage report...") is verified at the `FindingsReport` record level — a classicist
integration test asserts the `WikiCoverage` value on the record `LintRunCoordinator` passes to
`FindingsReportFormat.Build`, against a real run over a real temp-directory content root —
not by re-reading and parsing the written `.md` file. This keeps the format's "writer only"
posture intact rather than introducing parsing infrastructure this feature does not otherwise
need.
