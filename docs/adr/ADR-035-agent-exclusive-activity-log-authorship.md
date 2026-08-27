---
status: accepted
supersedes: ADR-028
superseded_by: []
reason: null
---

# ADR-035: Agent-Exclusive Authorship of the Wiki Activity Log

## Context and Problem Statement

The wiki's activity log (`log.md`) is the wiki's own record of what happened to it — the
counterpart to `index.md`, and therefore wiki content under Constitution Principle V: its
content is agent judgment exercised under versioned instruction files, never harness output.
Historically the harness co-authored it. `Grimoire.AgentRuntime.WikiLog.WikiLogAppender`
wrote a harness-generated entry whenever it could not find the run's correlation id in the
file, and unconditionally on Ingest failure; `Grimoire.Hub.OperationalState.RestartReconciler`
independently appended its own entry when a task was reconciled as failed on startup. Both
wrote with direct `File.AppendAllTextAsync` calls, deliberately outside the guarded tool
boundary — an explicitly allow-listed exemption in all three `*AgentGuardedWriteBoundaryRuleTests`.
The result was a file mixing agent-authored wiki content with harness run bookkeeping, which
is precisely the boundary Principle V draws.

This ADR decides one aspect: **who may author activity-log content**. The answer is the
agents, exclusively — no harness component may write to the resolved activity-log path.

## Decision Drivers

- **Constitution Principle V** — `log.md` content is wiki content. Which actions are worth
  logging, and what an entry says, is agent judgment under versioned instruction files. The
  harness may enforce the shape and placement of a write at the guarded boundary, but it may
  not author the text.
- **"Zero harness-authored activity-log content" is a 100% guarantee** — and the existing
  `*GuardedWriteBoundaryRuleTests` already provide the exact enforcement shape: an IL-level
  allow-list of namespaces permitted to call filesystem-write APIs. Removing an entry from
  that allow-list *is* the enforcement.
- **The backstop's one real diagnostic must survive its deletion** — "an agent changed the
  wiki and did not log it" was the only signal the harness writers carried that other records
  (task artifact, status history, conversation record, completion/failure events) do not
  cover. Its replacement must be derived mechanically from the harness's own record of
  allowed guarded writes, and must never write to the wiki.
- **Minimal surface** — a deletion, an allow-list removal, and one write-free observability
  signal. No new file, no new coordination layer, no new tool.

## Considered Options

1. **Delete both harness writers and enforce the deletion structurally** — remove
   `WikiLogAppender` and its call sites, remove the reconciler's log append, and drop the
   `Grimoire.AgentRuntime.WikiLog` exemption from the guarded-write allow-list.
2. Keep `WikiLogAppender` but make it write through the guarded boundary (rejected — the
   content would still be harness-authored, which is the actual violation; routing it through
   a guardrail launders the boundary rather than respecting it).
3. Keep the backstop only for failed runs (rejected — "changed nothing" is the criterion for
   *not* logging, and a failed run that changed nothing is the clearest case of a run that
   must leave no trace in wiki content).

## Decision Outcome

Chosen option: **Option 1 — agent-exclusive authorship, structurally enforced**, because it
is the only option in which no harness prose can reach wiki content by construction, and its
enforcement is an allow-list entry an existing structural test simply loses.

- `Grimoire.AgentRuntime.WikiLog.WikiLogAppender` is deleted, along with its call sites in
  `Grimoire.IngestAgent.Program` and `Grimoire.QueryAgent.Program` (success and failure
  paths in both).
- `Grimoire.Hub.OperationalState.RestartReconciler.AppendReconciliationLogAsync` is deleted;
  reconciliation continues to record the failure in the task artifact and the operational
  state history, and writes nothing to the wiki.
- The `Grimoire.AgentRuntime.WikiLog` entry is removed from `_allowedNamespacePrefixes` in
  `IngestAgentGuardedWriteBoundaryRuleTests`, `QueryAgentGuardedWriteBoundaryRuleTests`, and
  `LintAgentGuardedWriteBoundaryRuleTests`. The namespace survives (it hosts the write-free
  coverage observer, below) but may no longer call any filesystem-write API.
- Agents write the activity log exclusively through the guarded `write_file` tool, under
  their versioned instruction files (ADR-007); what to log and how to phrase it is agent
  judgment and is never asserted by a deterministic test.

Rule classification (Constitution Principle III):

| # | Rule | Category |
| --- | --- | --- |
| BR-1 | Filesystem-write APIs reachable from `Grimoire.IngestAgent`/`Grimoire.QueryAgent`/`Grimoire.LintAgent` and the shared `Grimoire.AgentRuntime` may be called only from `Grimoire.AgentRuntime.Guardrails*` and `Grimoire.AgentRuntime.Core.Adapters.Replay` — `Grimoire.AgentRuntime.WikiLog` is not exempt. | **Boundary Rule** — a dependency direction (who may reach the filesystem), durable across feature growth; enforced by the three existing IL-level `*GuardedWriteBoundaryRuleTests`, re-probed Red/Green after the allow-list entry's removal. |
| FSI-2 | No harness component writes to the resolved activity-log path: neither agent process on any exit path, nor the Hub's restart reconciler. | **Feature-Scoped Invariant** for the Hub half (BR-1 covers the agent assemblies structurally, but the Hub legitimately writes many other files, so its half is verified behaviourally): run the real reconciler against a temp content root containing an activity log and assert the file is byte-for-byte unchanged. |

### Consequences

- Good, because `log.md` is unambiguously agent-owned wiki content, restoring the Principle V
  boundary the backstop crossed; the boundary is held by an existing structural test that
  simply loses an exemption — the cheapest possible enforcement.
- Good, because the one diagnostic worth keeping survives as a harness-side signal that
  cannot, by construction, put harness prose into the wiki: the write-free
  `Grimoire.AgentRuntime.WikiLog.WikiLogCoverageObserver` runs once at run end in both
  writing agent processes, derives from the guarded-write journal whether the run made
  wiki-content writes without touching the activity log, and if so emits
  `wiki.log.change_not_logged` (WARN) and increments `wiki.log.unlogged_change_total`. It
  performs no I/O; the signal is an observability contract under Principle IV, not a rule of
  this ADR. The retired backstop's signals (`wiki.log.backstop_appended`,
  `wiki.log.backstop_appended_total`, `wiki_log.backstop_append`) are retired with it.
- Bad, because a run that changes the wiki and fails to log it leaves the wiki's own record
  incomplete where the backstop would have papered over it — accepted, and made visible
  rather than hidden: that is exactly what `wiki.log.change_not_logged` reports, and agent
  logging compliance is measured at an evaluation threshold instead of pretending a
  deterministic backstop made it 100%.
- Neutral, because activity logs written while the backstop existed keep their
  harness-authored entries; migration was explicitly refused, since a harness rewriting wiki
  content is the violation this decision removes.

## Change Triggers

- **Extensions (do not invalidate this ADR):** another agent gaining activity-log write
  capability through the guarded tools (a new consumer of an already-decided boundary);
  additional observability signals about log coverage alongside the existing
  coverage-observer pair.
- **Invalidations (would require full supersession):** reintroducing any harness-authored
  activity-log content, on any code path — including a harness backstop writer for failed or
  unlogged runs, whether it writes directly or through the guarded boundary.

## More Information

The prepend-only ordering check on activity-log writes is deliberately **not** decided here:
it is feature-scoped format content whose contract lives in
`specs/025-agent-owned-log/contracts/activity-log-write-contract.md`, not an architectural
decision. Read alongside [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md) (the guarded
tool boundary and write journal every activity-log write passes through),
[ADR-031](ADR-031-lint-full-wiki-write-scope.md) (which agents' policies place the activity
log in write scope), and [ADR-007](ADR-007-agent-instruction-surface.md) (the versioned
instruction files under which agents exercise logging judgment). Supersedes
[ADR-028](ADR-028-agent-owned-activity-log-prepend-ordering.md), whose ordering half is
owned by the feature contract above. Detailed rationale:
`specs/025-agent-owned-log/research.md`.
