# Data Model: Shared Foundation Prompt and Wiki-Identity Wizard

**Feature**: 029-shared-foundation-prompt | **Date**: 2026-09-05

Entities are files and in-process records; nothing here touches the operational-state database
(Principle V: durable state lives in files).

## 1. Foundation document

The single instruction document stating what kind of wiki this instance maintains, what it is for, and
the conventions that hold across every agent's work.

| Aspect | Value |
|---|---|
| Format | UTF-8 markdown, no frontmatter |
| Authored default | `backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md` — one source file, version-controlled product content |
| Delivered default | `<AgentDir>/<agentId>/Instructions/foundation-prompt.md`, one copy per agent, written by the agent build (ADR-043) |
| Instance document | `<DataDir>/foundation-prompt.md` — optional; when present it is the effective document for **every** agent |
| Validity | Readable and not effectively empty (whitespace-only counts as empty). Nothing validates what it *says* |
| Identity per run | The document's content hash, recorded per run distinguishably from the role document |

**Required shape** (what the drafting brief asks for, and what the default demonstrates): what this
wiki is for; what belongs in it and what does not; how pages are organised and named; the conventions
that hold across every agent's work. It states nothing about one agent's role.

## 2. Role document

Unchanged in kind: `backend/src/Grimoire.<Agent>Agent/Instructions/system-prompt.md`, delivered to
`<AgentDir>/<agentId>/Instructions/system-prompt.md`. It loses every statement that is true of the wiki
rather than of that agent's role (research.md R5 lists them) and keeps role, steps, write scope and
modes.

## 3. Composed instruction context

What an agent's system prompt actually is, after composition:

```text
<foundation document, verbatim>
<blank line>
<role document, verbatim>
```

- Exactly one blank line joins them (`"\n\n"`). No harness-authored header, label or banner.
- Order is identical for every agent type.
- Produced once per run, in `AgentHost`, before the run's first model turn.

**Invariant**: the composed text contains both documents' bytes unaltered, and its length equals the
sum of both documents' lengths plus the joiner.

## 4. Per-run instruction record

The run's record of what it operated under. The existing task-artifact `instruction_files` list carries
two entries instead of one — foundation first, then role — each with its path and content hash. The
list shape is unchanged, so existing readers are unaffected.

| Field | Meaning |
|---|---|
| `path` | The resolved path of the document as loaded |
| `sha256` | Its content hash, the version identity for that run |

## 5. Instance identity

The deployment-level fact of which foundation document is in effect.

| State | How it arises | How it is reported |
|---|---|---|
| `default` | No instance document exists | `source=default` plus the resolved per-agent path |
| `instance` | `<DataDir>/foundation-prompt.md` exists | `source=instance` plus that path, its hash and its first heading |

State transitions:

```text
default ──(wizard: --default)──────────────────► default        (nothing written)
default ──(wizard: --from-file)────────────────► instance
instance ─(wizard: --from-file, no --replace)──► instance       (refused, unchanged)
instance ─(wizard: --from-file --replace)──────► instance       (new content)
instance ─(operator deletes the file)──────────► default
```

There is no wizard action that deletes an instance document: removing one is a deliberate file
operation, not a menu entry, so the wizard can never leave an instance with less identity than it had.

## 6. Drafting brief

Produced by the wizard from the operator's description; consumed by an agent session outside the
system. It is **not** a foundation document and is never persisted as one.

| Part | Content |
|---|---|
| The operator's description | Quoted verbatim, unedited — it is the record of what was asked for |
| The required shape | The headings a foundation document carries and what each is for (§1) |
| Where the result goes | The invocation that hands the drafted document back |

The brief contains no statement about what a wiki should be — that judgment belongs to the drafting
agent. A brief that grows such a statement has crossed the Principle V line (plan.md, Agentic Boundary).

## 7. Wizard invocation

| Answer | Input | Effect |
|---|---|---|
| default | — | Nothing written. Reports that the instance stays on the shipped default |
| specialised | the operator's description | Emits a drafting brief. Nothing written |
| hand-back | a drafted document, optionally an explicit replace decision | Validates and persists it verbatim, or refuses |

Every answer is supplied with the invocation. The wizard never prompts, so a missing answer is a usage
failure naming what to pass, with or without a terminal attached.
