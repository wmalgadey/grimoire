---
title: Code Complexity Remediation Prompts
status: reference
role: source-material
binding_for_sdd: false
reader: manual prompting for /speckit-constitution, /speckit-specify, and direct CI/CD implementation work
usage: "Prompt library only; never cite as normative requirement in specs, plans, or ADRs. Findings become binding only once run through the referenced command and accepted (constitution amendment, Accepted ADR, or merged CI change)."
---

# Code Complexity Remediation Prompts

Ready-to-use prompts derived from `docs/code-complexity-analysis.md` (2026-08-06 report).
Grouped by which mechanism should carry the change, per the Document Map in `CLAUDE.md`:
constitution amendments for durable rules, spec-kit for architecturally significant
work, direct CI/CD edits for mechanical gate additions. Run one prompt at a time.

## Usage

```text
/speckit-constitution <paste Prompt C1>
/speckit-specify <paste one of Prompt S1-S3>
```

CI/CD prompts (Section 3) are not slash commands — hand them to a coding agent
(or do the edit yourself) as a direct implementation task, referencing the exact
files named in each prompt.

---

## Section 1 — Constitution amendment

### Prompt C1: CI-enforced complexity ceiling + stricter guardrail threshold

```text
Amend the constitution with two related additions, both motivated by
docs/code-complexity-analysis.md (source material only, do not cite it as a
requirement — extract the rule itself, not the document).

1. Extend Principle IV (Behavioral & Observable Engineering) with a general
   complexity ceiling: cyclomatic complexity per function MUST NOT exceed 15
   in merged code, with 11-15 treated as a warning band. This threshold is
   enforced by a named CI gate (see the corresponding CI task) — per this
   principle's own rule, a threshold not enforced by CI/CD does not exist.
   Apply to both backend (C#) and frontend (TypeScript/JavaScript/Svelte).

2. Extend Principle V (Agentic Core & Deterministic Harness), in the
   "Guardrails at the tool boundary" subsection: guardrail-evaluation logic
   that decides whether an agent write/read is allowed (the deny-by-default
   decision path) MUST be held to a stricter cyclomatic complexity ceiling
   than general code (CC <= 10 per function), with each write-mode / policy
   branch implemented as an independently testable unit. Rationale: a wrong
   branch in this code is a guardrail bypass, not an ordinary bug, so it
   deserves a tighter bound than Principle IV's general ceiling.

Bump the version per semantic versioning (this is a MINOR change — new
enforceable constraints, no removal/redefinition of existing principles).
Update the Sync Impact Report and propagate to .specify/templates/ as usual.
```

---

## Section 2 — Spec-kit work

### Prompt S1: Guardrail write-mode decomposition (safety-critical, P1)

```text
Create a new feature spec named "guard-write-mode-decomposition".

Goal:
Decompose SharedFileWriteGuard.EvaluateWriteAsync (backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs:144),
currently cyclomatic complexity 34, into one named method per WriteMode
variant plus a separate lock-acquisition/disposal path, without changing
observable guardrail behavior.

Business intent:
This function enforces the write guardrail at the agent tool boundary
(Constitution Principle V). Its current size makes the deny-by-default
paths hard to verify by inspection. Splitting it makes each policy branch
independently testable and keeps it under the guardrail-specific complexity
ceiling (Principle V amendment, see docs/adr and constitution).

In scope:
- One private method per WriteMode variant, each independently unit-testable.
- Lock acquisition/disposal isolated from write-mode decision logic.
- No change to which writes are allowed or denied for any existing WriteMode.
- Test coverage for each extracted method at least as complete as the
  current guardrail integration tests cover the combined function today.

Out of scope:
- Adding new WriteMode variants or changing guardrail policy.
- Touching GuardedToolExecutor or other guardrail components not part of
  this function.

Constraints:
- This is harness code (Principle V "deterministic harness" half) — no
  wiki-content judgment is involved, so no agent-behavior evaluation tests
  are needed, only deterministic guardrail integration tests.
- Must not regress the existing guardrail integration test suite; extend
  it if the decomposition reveals a previously-implicit branch that lacked
  its own test.
- Reference ADR-006 (agent-tool-loop-guarded-boundary) as the governing
  boundary contract; this spec must not require changes to that ADR.
```

### Prompt S2: Record/markdown parser robustness (P1/P2)

```text
Create a new feature spec named "harness-parser-robustness".

Goal:
Reduce the complexity of the repository's hand-written line-based record
and markdown parsers by extracting one named function per section/field
group, and factor out the shared "skip blanks, detect header, accumulate
section" loop that several of them currently re-implement independently.

Targets (from docs/code-complexity-analysis.md, do not cite the doc itself
as a requirement in the spec — restate the targets directly):
- ConversationRecordFormat.TryParseBookkeeping (CC 53) and
  ConversationRecordFormat.Parse (CC 27),
  backend/src/Grimoire.Hub/QueryConversations/ConversationRecordFormat.cs
- TaskArtifactStore.ParseMarkdown (CC 29),
  backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs

Business intent:
These parsers sit on the harness side (record/artifact persistence, not
wiki-content judgment), so Principle V is not implicated. The goal is
reduced defect risk as record schemas evolve — every new field currently
multiplies paths through a single 50+ branch function.

In scope:
- Per-section/per-field extraction within each parser, preserving current
  parsing output byte-for-byte for all existing valid and invalid inputs.
- A shared parsing helper (e.g. for "skip blanks, detect header, accumulate
  section") usable by both ConversationRecordFormat and TaskArtifactStore,
  if the shared shape survives closer inspection during planning.
- Test coverage for each extracted function at least as complete as the
  current combined-function tests.

Out of scope:
- Changing the on-disk record/markdown format itself.
- Touching RemediationTaskRecordFormat.Parse (CC 18) or other parsers not
  listed above — track those separately if this pattern proves out.

Constraints:
- Deterministic, hermetic tests only (this is harness code per Principle II).
- If the shared helper introduces a new cross-cutting parsing abstraction
  used by 3+ call sites, flag during /speckit-plan whether it rises to the
  level of a new structural pattern requiring an ADR (Principle III) —
  a shared internal helper likely does not, but confirm during planning
  rather than assuming.
```

### Prompt S3: EvalRunner capture/replay pipeline consolidation (P2, ADR-triggering)

```text
Create a new feature spec named "eval-runner-pipeline-consolidation".

Goal:
Consolidate the near-duplicated EvalRunner capture/replay pipeline family
behind a single generic pipeline skeleton parameterized by a scenario
descriptor, replacing per-scenario-type pipeline classes and the manual
subcommand switch that dispatches over them.

Targets (from docs/code-complexity-analysis.md, restate directly, do not
cite the doc as a requirement):
- CapturePipeline (CC 25), backend/src/Grimoire.EvalRunner/Capture/CapturePipeline.cs:65
- QueryCapturePipeline (CC 20), backend/src/Grimoire.EvalRunner/Capture/QueryCapturePipeline.cs:56
- LintCapturePipeline (CC 14) and RemediationReVerificationCapturePipeline (CC 12),
  same directory
- The matching replay pipelines (ReplayPipeline CC 19, QueryReplayPipeline)
- The 201-line, CC-25 subcommand switch in
  backend/src/Grimoire.EvalRunner/Program.cs:47

Business intent:
These four-plus pipeline classes share the same run-scenario / collect-
artifacts / score shape. Consolidating them removes more aggregate
complexity than any single-function fix and turns the Program.cs switch
into a lookup table, but it is a new integration pattern (a generic
pipeline over a scenario descriptor replacing several concrete classes),
so per Constitution Principle III this requires a drafted ADR reaching
Accepted status before /speckit-tasks.

In scope:
- A single pipeline abstraction (template method or generic-over-descriptor)
  covering capture and replay for all current scenario types (query, lint,
  remediation re-verification, and the base scenario type).
- A dispatch table replacing the Program.cs subcommand switch.
- Behavior parity: identical captured artifacts and scores for existing
  eval scenarios before and after consolidation.

Out of scope:
- Changing what is captured/scored for any scenario type.
- New scenario types (this spec is about the reusable skeleton, not new
  eval capabilities).

Constraints:
- Check ADR-012 (eval-runner-recorded-replay) during planning — the new
  ADR drafted for this spec must reconcile with, not duplicate, ADR-012's
  existing decisions about the capture/replay model.
- Deterministic, hermetic tests only; this is harness orchestration code
  per Principle II, no agent-judgment evaluation tests apply here.
- Per Principle III, do not proceed to /speckit-tasks until the drafted
  ADR for the generic pipeline pattern is Accepted.
```

---

## Section 3 — CI/CD additions (direct implementation, not spec-kit)

These are mechanical gate additions enforcing thresholds already established
by Prompt C1. Land them together with, or immediately after, the constitution
amendment — a gate enforcing a threshold the constitution doesn't yet state
has no normative basis.

### Prompt CI1: Backend cyclomatic-complexity gate

```text
Add a cyclomatic-complexity CI gate to the "Deterministic Backend Gates" job
in .github/workflows/ci.yml, alongside the existing "Run linting and
formatting checks" step.

Requirements:
- Fail the build if any function in backend/src exceeds CC 15, matching the
  threshold in Constitution Principle IV (see Prompt C1's amendment).
- Implementation choice: either (a) add a `lizard backend/src -l csharp
  --CCN 15` step (pip-installable, matches the tool already used to produce
  docs/code-complexity-analysis.md), or (b) enable Roslyn analyzers
  CA1502 (cyclomatic complexity) and CA1505 (maintainability index) as
  build-breaking (not warnings) via backend/Directory.Build.props and
  backend/.editorconfig, since neither currently configures them.
  Prefer (b) if the team wants IDE-time feedback in addition to CI;
  prefer (a) if a zero-config CI-only gate is preferred. Do not add both.
- The gate must run on every pull_request, matching the existing job trigger.
```

### Prompt CI2: Stricter complexity gate for guardrail namespace

```text
Extend the CI gate from Prompt CI1 (or add a second, scoped step) to enforce
CC <= 10 specifically for backend/src/Grimoire.AgentRuntime/Guardrails/**,
matching the stricter guardrail threshold added to Principle V in Prompt C1.
If using lizard, this is a second invocation scoped to that path
(`lizard backend/src/Grimoire.AgentRuntime/Guardrails --CCN 10`); if using
Roslyn analyzers, this needs a folder-scoped .editorconfig override under
that directory with a lower CA1502 threshold. Fail the build on violation,
same as Prompt CI1.
```

### Prompt CI3: Frontend complexity/size lint rules

```text
Add ESLint rules to frontend/eslint.config.js enforcing the same ceiling as
the backend gate, to lock in the frontend's current clean state (max CC 8,
no function over 100 lines or 4 parameters per docs/code-complexity-analysis.md)
before it regresses:
- `complexity: ["error", 15]`
- `max-params: ["error", 4]`
- `max-lines-per-function: ["error", 100]`
Wire these into the existing "Lint and format check" step in the "Frontend
Gates" job in .github/workflows/ci.yml — no new CI step needed, this should
fail the build via the lint step that already runs there.
```

### Prompt CI4: Maintainability-index regression gate

```text
Add a CI check that fails the build if any backend/src file's maintainability
index drops below 20 (the critical threshold already defined in
docs/code-complexity-analysis.md's methodology section). All files are
currently above this threshold, so this is a pure regression gate, not a
remediation requirement. Implement as a lizard-based or Roslyn CA1505-based
check per the same tooling choice made in Prompt CI1, run in the same
"Deterministic Backend Gates" job.
```
