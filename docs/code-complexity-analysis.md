# Code Complexity Analysis

**Role**: Source material for refactoring prioritization and code review; not binding for SDD (see Document Map in `CLAUDE.md`).
**Reader**: Developers and reviewers deciding which code areas need refactoring or extra test attention.
**Date**: 2026-08-06 · **Scope**: `backend/src` (C#), `backend/tests` (C#), `frontend/src` (TypeScript/JavaScript/Svelte)

## Methodology

Metrics computed per function with [lizard](https://github.com/terryyin/lizard) 1.23.0 (cyclomatic complexity, NLOC, function length, parameter count) and per file with a Pygments-token-based Halstead calculator (volume, difficulty, effort, estimated bugs = volume/3000). The maintainability index uses the Oman/Hagemeister formula with Microsoft's 0–100 normalization, computed from per-function averages within each file:

`MI = max(0, (171 − 5.2·ln(avg volume) − 0.23·avg CC − 16.2·ln(avg NLOC)) · 100/171)`

Thresholds applied:

| Metric | Good | Acceptable | Refactoring candidate | High priority |
| --- | --- | --- | --- | --- |
| Cyclomatic complexity (per function) | ≤ 5 | 6–10 | 11–15 | > 15 |
| Maintainability index (per file) | > 70 | 20–70 | — | < 20 |
| Function length | ≤ 100 lines | — | > 100 lines | — |
| Parameter count | ≤ 4 | — | > 4 | — |

Caveats: the Halstead/MI values are file-level approximations (token classification via Pygments, not a full Roslyn/TS parse); function names reported by lizard occasionally misattribute the enclosing type — file and line numbers are authoritative. Auto-generated files are not excluded but the scanned trees contain none. Svelte components are lexed as TypeScript, so metrics there cover script blocks reliably and markup only approximately.

## Summary

| Scope | Files | Functions | Avg CC | Max CC | CC > 10 | Avg MI | Functions > 100 lines | Functions > 4 params |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `backend/src (C#)` | 145 | 915 | 3.43 | 53 | 45 | 56.0 | 43 | 109 |
| `backend/tests (C#)` | 169 | 1051 | 1.98 | 39 | 12 | 51.2 | 10 | 22 |
| `frontend/src (TS/JS/Svelte)` | 32 | 401 | 1.3 | 8 | 0 | 62.5 | 0 | 0 |

### Cyclomatic complexity distribution — backend/src (C#)

| Band | Functions | Share |
| --- | --- | --- |
| 1-5 (simple) | 765 | 83.6% |
| 6-10 (moderate) | 105 | 11.5% |
| 11-15 (complex) | 27 | 3.0% |
| >15 (high priority) | 18 | 2.0% |

### Cyclomatic complexity distribution — backend/tests (C#)

| Band | Functions | Share |
| --- | --- | --- |
| 1-5 (simple) | 983 | 93.5% |
| 6-10 (moderate) | 56 | 5.3% |
| 11-15 (complex) | 7 | 0.7% |
| >15 (high priority) | 5 | 0.5% |

### Cyclomatic complexity distribution — frontend/src (TS/JS/Svelte)

| Band | Functions | Share |
| --- | --- | --- |
| 1-5 (simple) | 394 | 98.3% |
| 6-10 (moderate) | 7 | 1.7% |
| 11-15 (complex) | 0 | 0.0% |
| >15 (high priority) | 0 | 0.0% |

## Hotspots — `backend/src`

Top functions by cyclomatic complexity (CC > 15 = high priority):

| CC | Length | Params | Function | Location |
| --- | --- | --- | --- | --- |
| 53 | 190 | 3 | `TryParseBookkeeping` | `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordFormat.cs:345` |
| 34 | 110 | 4 | `EvaluateWriteAsync` | `backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs:144` |
| 29 | 96 | 1 | `ParseMarkdown` | `backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs:140` |
| 27 | 147 | 1 | `Parse` | `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordFormat.cs:170` |
| 25 | 160 | 4 | `RunScenarioAsync` | `backend/src/Grimoire.EvalRunner/Capture/CapturePipeline.cs:65` |
| 25 | 201 | 1 | `switch` | `backend/src/Grimoire.EvalRunner/Program.cs:47` |
| 22 | 48 | 1 | `SynthesisDeclineEditRequest` | `backend/src/Grimoire.EvalRunner/Scoring/QueryDeterministicScorers.cs:188` |
| 22 | 66 | 1 | `ParseTerminalEvent` | `backend/src/Grimoire.EvalRunner/Workspace/QueryAgentProcessInvoker.cs:217` |
| 20 | 130 | 4 | `RunScenarioAsync` | `backend/src/Grimoire.EvalRunner/Capture/QueryCapturePipeline.cs:56` |
| 20 | 136 | 3 | `ExecuteWriteFileAsync` | `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs:227` |
| 19 | 116 | 6 | `ReplaySampleAsync` | `backend/src/Grimoire.EvalRunner/Replay/ReplayPipeline.cs:109` |
| 19 | 131 | 4 | `RunAsync` | `backend/src/Grimoire.AgentRuntime/Core/AgentLoop.cs:84` |
| 18 | 19 | 1 | `Equals` | `backend/src/Grimoire.Hub/QueryConversations/RecordedTurn.cs:63` |
| 18 | 93 | 1 | `Parse` | `backend/src/Grimoire.Hub/RemediationTasks/RemediationTaskRecordFormat.cs:192` |
| 18 | 106 | 5 | `SuperviseAsync` | `backend/src/Grimoire.Hub/RemediationTasks/RemediationRunCoordinator.cs:223` |

### Files with the lowest maintainability index

All files stay above the critical MI threshold of 20; the following are the weakest (MI < 50, meaning above-average volume and branching per function):

| MI | Max CC | Functions | Halstead volume | Est. bugs | File |
| --- | --- | --- | --- | --- | --- |
| 39.1 | 16 | 6 | 16172 | 5.39 | `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs` |
| 40.6 | 20 | 3 | 7508 | 2.5 | `backend/src/Grimoire.EvalRunner/Capture/QueryCapturePipeline.cs` |
| 41.4 | 53 | 15 | 32017 | 10.67 | `backend/src/Grimoire.Hub/QueryConversations/ConversationRecordFormat.cs` |
| 42.3 | 19 | 5 | 10753 | 3.58 | `backend/src/Grimoire.EvalRunner/Replay/ReplayPipeline.cs` |
| 42.6 | 9 | 2 | 3792 | 1.26 | `backend/src/Grimoire.AgentRuntime/Host/AgentHost.cs` |
| 43.1 | 12 | 3 | 6443 | 2.15 | `backend/src/Grimoire.EvalRunner/Capture/RemediationReVerificationCapturePipeline.cs` |
| 43.2 | 14 | 3 | 6420 | 2.14 | `backend/src/Grimoire.EvalRunner/Capture/LintCapturePipeline.cs` |
| 43.8 | 7 | 5 | 13854 | 4.62 | `backend/src/Grimoire.Hub/Program.cs` |
| 43.9 | 14 | 19 | 32442 | 10.81 | `backend/src/Grimoire.Hub/AgentDispatch/Adapters/AgentProcess/AgentProcessHost.cs` |
| 44.4 | 13 | 5 | 10206 | 3.4 | `backend/src/Grimoire.EvalRunner/Replay/QueryReplayPipeline.cs` |
| 44.6 | 7 | 2 | 2782 | 0.93 | `backend/src/Grimoire.Hub/IngestSubmission/BoardEndpoints.cs` |
| 44.8 | 22 | 7 | 11762 | 3.92 | `backend/src/Grimoire.EvalRunner/Workspace/QueryAgentProcessInvoker.cs` |

### Functions with more than 4 parameters

109 functions exceed 4 parameters. The worst offenders:

| Params | CC | Function | Location |
| --- | --- | --- | --- |
| 11 | 13 | `RunRemediationExecutionAsync` | `backend/src/Grimoire.EvalRunner/Workspace/LintAgentProcessInvoker.cs:219` |
| 11 | 7 | `GuardedToolExecutor` | `backend/src/Grimoire.AgentRuntime/Guardrails/GuardedToolExecutor.cs:82` |
| 11 | 5 | `LintRunCoordinator` | `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs:57` |
| 10 | 1 | `IngestIntentHandler` | `backend/src/Grimoire.IngestAgent/Program.cs:135` |
| 9 | 8 | `FinalizeFailedAsync` | `backend/src/Grimoire.IngestAgent/Program.cs:369` |
| 9 | 12 | `RunAsync` | `backend/src/Grimoire.EvalRunner/Workspace/QueryAgentProcessInvoker.cs:87` |
| 9 | 16 | `FinishRunAsync` | `backend/src/Grimoire.Hub/LintDispatch/LintRunCoordinator.cs:254` |
| 8 | 13 | `RunAsync` | `backend/src/Grimoire.EvalRunner/Workspace/AgentProcessInvoker.cs:89` |
| 8 | 3 | `AgentLoop` | `backend/src/Grimoire.AgentRuntime/Core/AgentLoop.cs:30` |
| 8 | 4 | `IngestRunCoordinator` | `backend/src/Grimoire.Hub/IngestDispatch/IngestRunCoordinator.cs:47` |

## Findings

### Overall assessment

The codebase is in good structural health. 83.6% of backend source functions sit in the
"simple" band (CC ≤ 5, average 3.43), the frontend is uniformly simple (maximum CC 8, no
function over 100 lines or 4 parameters), and no file falls below the critical
maintainability threshold of 20. Complexity is not spread out; it is concentrated in a
small number of identifiable hotspots (18 backend functions above CC 15, i.e. under 2%).

### 1. Hand-written line-based parsers are the dominant complexity source

The four highest-complexity functions are all record/markdown parsers:

- `ConversationRecordFormat.cs` — `TryParseBookkeeping` (CC 53) and `Parse` (CC 27) in
  one file, which also has the highest Halstead bug estimate in `Grimoire.Hub`.
- `TaskArtifactStore.ParseMarkdown` (CC 29) in `Grimoire.IngestAgent`.
- `QueryAgentProcessInvoker.ParseTerminalEvent` (CC 22) in `Grimoire.EvalRunner`.

Parsers are legitimately branchy, but at CC 50+ every new record field multiplies the
paths to test. Recommended refactoring: extract one named function per section/field
group (guard-clause style already used elsewhere in the codebase), or introduce a small
shared line-record parsing helper — several of these parsers re-implement the same
"skip blanks, detect header, accumulate section" loop.

### 2. `SharedFileWriteGuard.EvaluateWriteAsync` (CC 34) is safety-critical complexity

This function enforces the write guardrail at the tool boundary (Constitution
Principle V). Its complexity comes from handling all `WriteMode` variants plus lock
acquisition/disposal in one method. Because a wrong branch here is a guardrail bypass,
this is the highest-value refactoring target: splitting per-`WriteMode` evaluation into
named private methods would reduce CC per unit below 10 and make the deny-by-default
paths individually testable. Until then, this function statistically deserves the
densest test coverage in the repository (it currently is well covered by the
guardrail integration tests — keep it that way when touching it).

### 3. The EvalRunner capture/replay pipeline family is near-duplicated

`CapturePipeline` (CC 25), `QueryCapturePipeline` (CC 20), `LintCapturePipeline`
(CC 14) and `RemediationReVerificationCapturePipeline` (CC 12) — plus the matching
replay pipelines — occupy 6 of the 15 lowest-MI slots and share the same
run-scenario/collect-artifacts/score shape. The 201-line, CC-25 subcommand `switch` in
`EvalRunner/Program.cs` dispatches over the same family. Consolidating the shared
skeleton (template method or a generic pipeline over a scenario descriptor) would
remove more aggregate complexity than any single-function refactoring, and would turn
the `switch` into a lookup table.

### 4. Parameter counts: mostly DI constructors, three real offenders

109 backend functions exceed 4 parameters, but the bulk are constructors receiving
injected dependencies — conventional in .NET and low risk (CC mostly 1–5). Three are
genuine refactoring candidates because they are *methods* with high parameter counts
and non-trivial branching: `LintAgentProcessInvoker.RunRemediationExecutionAsync`
(11 params, CC 13), `QueryAgentProcessInvoker.RunAsync` (9 params, CC 12), and
`LintRunCoordinator.FinishRunAsync` (9 params, CC 16). Each would benefit from a
parameter object (e.g. a run-context record), which the codebase already uses in
similar places. Constructors with 8–11 dependencies (`GuardedToolExecutor`,
`LintRunCoordinator`, `IngestIntentHandler`) are also a soft signal that those classes
aggregate several responsibilities.

### 5. Frontend: clean, with one small duplication signal

No frontend function exceeds CC 8. The only recurring pattern is `parseErrorMessage`
duplicated across `lintApi.ts`, `remediationApi.ts`, `querySubmissionApi.ts` and `ingestSubmissionsApi.ts`
(CC 6–8 each) and near-identical `apply*LifecycleEvent` reducers in the lifecycle
clients. A shared helper would remove the duplication; complexity itself is not a
concern.

### 6. Tests: two heavyweight helpers in ArchTests

Test code is simple on average (CC 1.98), as expected for state-based classicist tests.
The outliers are hand-rolled C# scanners, now extracted into the shared
`ArchScan.Tokenize`/`ArchScan.ScanString` (`Tokenize` CC 39, `ScanString` CC 25) used by
`RetiredPagesWrapperPathRuleTests` and `HarnessSurfaceScopeRuleTests`
(022-align-wiki-structure). They are structural-test infrastructure, not product code,
but at this size they warrant either their own focused tests or further decomposition,
since a bug in the scanner silently weakens the boundary rules it powers.

### Prioritized refactoring backlog

| Priority | Target | Metric driver | Suggested technique |
| --- | --- | --- | --- |
| P1 | `SharedFileWriteGuard.EvaluateWriteAsync` | CC 34, guardrail-critical | Extract per-`WriteMode` methods |
| P1 | `ConversationRecordFormat.TryParseBookkeeping` / `Parse` | CC 53 / 27, highest bug estimate | Extract per-section parsing functions |
| P2 | EvalRunner capture/replay pipeline family + `Program.cs` switch | 6 low-MI files, CC 25 switch | Generic pipeline skeleton + dispatch table |
| P2 | `TaskArtifactStore.ParseMarkdown` | CC 29 | Extract per-section parsing functions |
| P3 | `RunRemediationExecutionAsync`, `RunAsync`, `FinishRunAsync` | 9–11 params with CC 12–16 | Parameter objects (run-context records) |
| P3 | Frontend `parseErrorMessage` / lifecycle reducers | Duplication across 3+ modules | Shared helper module |
| P3 | ArchTests `Tokenize`/`ScanString` helpers | CC 39 / 25 in test infra | Shared, self-tested ArchTests utility |

None of these refactorings move agent judgment into backend code — all hotspots are
harness code (parsing, dispatch, guardrails), so Principle V is unaffected.

### Optional follow-up: CI enforcement

Per Constitution Principle IV, thresholds only exist once CI enforces them. If the team
wants to hold the line at CC ≤ 15 (or ≤ 10 for new code), candidate gates are `lizard
--CCN 15` in the PR pipeline or Roslyn analyzers CA1502/CA1505/CA1506. Introducing such
a gate is a process decision and is intentionally **not** part of this analysis.

### Reproducing this analysis

```bash
pip install lizard
lizard backend/src -l csharp --CCN 10          # cyclomatic complexity, NLOC, params
lizard frontend/src -l typescript -l javascript
```

Halstead and maintainability-index figures were computed with a Pygments-based
tokenizer over the same file set; the exported per-file data lives in
`docs/code-complexity-analysis.json`.
