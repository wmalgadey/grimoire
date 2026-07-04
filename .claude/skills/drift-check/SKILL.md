---
name: drift-check
description: Audit the current implementation and active specs against docs/decision-context-overview.md (product vision and problem space) and the constitution to detect drift early and route findings into constitution/ADR amendments. Use when the user asks for a drift check or vision alignment, or before starting a new spec.
context: fork
agent: general-purpose
allowed-tools: Read, Grep, Glob, Bash
disable-model-invocation: true
---

# drift-check — Vision/Implementation Alignment Audit

Feature specs are locally consistent; drift is only visible against the product
vision. This skill compares code and active specs with
`docs/decision-context-overview.md` and `.specify/memory/constitution.md`, and routes
every finding into the SDD process. **Assessment only — propose, never apply changes.**

## User Input

```text
$ARGUMENTS
```

You **MUST** consider the user input before proceeding (if not empty).

## Procedure

1. **Read the reference documents in full**:
   - `docs/decision-context-overview.md` — especially §0: North Star Outcomes, The
     Agentic Boundary, Autonomy Ladder, Scale & Usage Assumptions, What This Is Not.
   - `.specify/memory/constitution.md` — all principles, especially II (testing split)
     and V (Agentic Core & Deterministic Harness).
   - All ADRs in `docs/adr/` (the decision inventory).
2. **Survey the implementation** (backend, agents, frontend where present):
   - Where do LLM calls happen? A single structured-output call inside a fixed
     control flow is a pipeline, not an agent — agents must be LLM loops with tools.
   - Are the agents' instruction files (CLAUDE.md/SKILL.md) actually loaded into the
     agent's working context — or merely read, hashed, or logged (compliance theater)?
   - Are guardrails enforced at the agent's tool boundary (deny-by-default, at the
     moment of the tool call) — or as post-hoc validation of pipeline output?
   - Where is wiki-content judgment made (update-vs-create, supersession,
     categorization, tagging, confidence, index/log content)? Any deterministic
     implementation of such judgment in backend code (string matching, rule tables,
     classifiers, content templating) violates Principle V.
3. **Check active specs** (`specs/*/spec.md` on unmerged branches):
   - Success criteria that attach 100% deterministic guarantees to agent judgment
     (spec defect per Principle II).
   - Requirements that turn instruction-set content (wiki conventions) into system
     features.
4. **Check the vision document itself against reality**:
   - Stale claims: paths, layouts, or mechanisms stated in the vision that do not and
     will not exist.
   - Binding statements not yet extracted into the constitution or an Accepted ADR
     (unenforced vision is future drift).
5. **Check North Star alignment**: do recent/planned features move at least one North
   Star Outcome? Structure without outcome movement is drift by definition (§0).

## Known drift signatures (2026-07-04 incident)

- "Pipeline" vocabulary used for agent execution in docs, ADRs, or specs.
- Instruction files recorded for audit but not governing the agent.
- Guardrails wrapped around the system's own deterministic code instead of an
  autonomous actor.
- Wiki judgment reimplemented as regex/lookup ("semantic" in name only).
- Growing C# surface for behavior that should be an instruction-file edit.

## Report format

For each finding report:

1. **Reference** — the violated vision section or constitution principle.
2. **Evidence** — file:line, with a one-sentence description.
3. **Severity** — (a) violates an enforced principle, (b) contradicts the vision but
   is not yet enforced, (c) vision document is stale.
4. **Routing** — exactly one recommended action:
   - (a) → fix task against code or the active spec.
   - (b) → constitution amendment (`/speckit-constitution`) or new ADR, so the vision
     statement gains enforcement.
   - (c) → update `docs/decision-context-overview.md` (only if reality is the desired
     state — otherwise it is drift, not staleness).

End with a one-line verdict (aligned / drifting) and the single most important action.

## Constraints

- Never propose weakening the constitution to legalize existing drift; that trade-off
  is the user's explicit decision.
- Findings belong in the conversation (and optionally in `dev-experience.md` via
  `/dev-log`) — never in specs or wiki content.
- This skill runs in a forked, isolated context: the final report is the only output
  returned to the main conversation, so it must be fully self-contained — no references
  to intermediate tool output or "see above".
