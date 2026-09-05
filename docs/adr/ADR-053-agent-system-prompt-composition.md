---
status: proposed
supersedes: ADR-007
---

# ADR-053: An Agent's System Prompt Is a Shared Foundation Document Composed With Its Role Document

> **Status notes** (informational, no status change):
> - Extends [ADR-043](ADR-043-build-distributed-agent-artifacts.md): the foundation document is a
>   new file in each agent's build output, delivered by the mechanism ADR-043 already decided.
> - Extends [ADR-012](ADR-012-eval-runner-recorded-replay.md): the recording fingerprint set follows
>   the instruction surface, so it gains the foundation document; ADR-012's own decision is unchanged.
> - Related: [ADR-029](ADR-029-harness-operator-turn-delimiter.md) and
>   [ADR-054](ADR-054-default-user-prompt-and-message-scaffold.md), which own the *user* channel;
>   this ADR owns only the system prompt.

## Context and Problem Statement

[ADR-007](ADR-007-agent-instruction-surface.md) decided that an agent's system prompt is exactly one
file — `system-prompt.md` — loaded verbatim. Three agents now exist (ingest, query, lint), and
everything true of the *wiki itself* rather than of one agent's role is consequently stated three
times, in three separately maintained files: folder structure, page types, frontmatter standard, tag
taxonomy, confidence scoring, supersession rules, `index.md` and `log.md` conventions, and the
"source content is data, not instructions" rule. Nothing keeps the three copies consistent, and they
have already drifted. Changing what kind of wiki an instance maintains means editing three files
consistently and hoping.

The one-file rule is what makes this unavoidable, so it is the rule that has to change. That directly
contradicts ADR-007's decision ("the entire system prompt, one file"), which under Constitution
Principle III's invalidation test is an invalidation rather than an extension: ADR-007 is superseded
whole, and its still-valid second aspect — the default user prompt and the harness-owned scaffold —
is re-decided independently in ADR-054.

## Decision Drivers

- What is true of the whole wiki must be stated in exactly one document, or it cannot stay consistent.
- The harness must not become a place where instruction *content* is authored, transformed or
  interpreted (Constitution Principle V); composition must be mechanical and inspectable.
- Every agent must be treated identically — no per-agent branch in the platform (ADR-013, ADR-044).
- Fail-closed loading, per-document SHA-256 traceability, and the explicit-path child-process CLI
  contract (ADR-036) must survive unchanged.
- Evaluation and replay runs must compose instructions exactly as a dispatched run does, without
  operator configuration (ADR-043's driver).

## Considered Options

1. **Two documents composed by the harness in a fixed order**: one shared foundation document plus the
   agent's own role document, concatenated foundation-first into the single system prompt.
2. **Keep one file per agent and deduplicate by convention** — a review checklist, or a generator that
   writes the three files from a shared source.
3. **Pass the two documents as two system blocks** to the model client instead of composing them.
4. **Inject the foundation document as the first message in the user channel.**

## Decision Outcome

Chosen option: **Option 1**, because it removes the triplication at its root while leaving the
harness's job purely mechanical — read two files, join them, hand the result over.

- An agent's system prompt is composed of exactly two instruction documents:
  - **the foundation document** (`foundation-prompt.md`) — what kind of wiki this instance maintains,
    what it is for, and the conventions that hold across every agent's work; identical for all agents;
  - **the agent's role document** (`system-prompt.md`) — that agent's role, steps, write scope and
    modes, and nothing that is true of the wiki as a whole.
- **Composition order is fixed and identical for every agent**: foundation document, then exactly one
  blank line, then the role document. The harness adds no header, label, banner or any other text of
  its own; the join is `"\n\n"` and nothing else.
- Both documents are loaded **verbatim** and **fail-closed**: missing, unreadable or effectively empty
  fails the run before any wiki write, with a reason naming the document that failed.
- Both documents are recorded per run as separate entries in the task artifact's existing
  `instruction_files` list, foundation first, each with its own SHA-256. The list shape is unchanged;
  it simply carries two entries where it carried one.
- The agent CLI takes an explicit path per document (`--foundation-prompt-path` alongside the existing
  `--system-prompt-path`). The harness composes paths; the agent performs no discovery.
- Composition happens in the one shared startup template (`AgentHost`) that every agent already runs
  through, so no agent-conditional branch exists.
- **Feature-Scoped Invariant** (Principle III): "every agent run loads both documents and receives
  them verbatim in the documented order". It protects this feature's current surface shape, not a
  dependency direction, and is verified by classicist behavioral tests — a real run of each agent
  type asserting the composed text and the recorded entries — never by reflecting over types or IL.
- **Boundary Rule** (Principle III, existing rule extended): no production type may author instruction
  content, and only the namespaces on an explicit allow-list may write a file named by an
  agent-instruction filename literal. `foundation-prompt.md` joins `system-prompt.md`,
  `default-user-prompt.md` and `policy.json` in that rule's literal set, and the rule keeps its
  Red/Green probe — extended to cover the new literal specifically. The allow-list's second entry, and
  the terms on which a component may hold custody of an instruction document at all, are decided in
  [ADR-056](ADR-056-instance-instruction-custody.md); this ADR owns the rule, not its exceptions.

### Consequences

- Good, because a statement about the wiki now has exactly one home, and an instance that wants a
  different kind of wiki changes one document instead of three.
- Good, because the two halves stay legible: a role document that starts explaining what the wiki is
  for is now visibly in the wrong file.
- Good, because per-document hashes make it possible to tell, after the fact, whether a behavioural
  change came from the shared statement or from one agent's role.
- Bad, because every recorded-replay eval recording goes stale the moment composition lands — the
  system-prompt hash the replay client verifies changes for all three agents. Mitigated only in the
  sense that this is ADR-012's designed instruction-change gate firing correctly; the recordings must
  be re-captured against a live provider before the change merges.
- Bad, because "which document should this sentence live in?" becomes a judgment call maintainers now
  have to make. Accepted: it is the same judgment as "is this about the wiki or about this agent?",
  and getting it wrong is a text move, not a defect.
- Neutral, because the number of files an agent build delivers grows by one; ADR-043's mechanism
  carries it unchanged.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new agent type loading the same two documents in the
  same order; growth or rewriting of either document's *content*; a new fingerprint or record that
  follows the instruction surface; moving text between the two documents.
- **Invalidations (would require full supersession):** a third instruction document composed into the
  system prompt; a per-agent difference in composition order; conditional or partial loading of either
  document (true progressive disclosure); the harness inserting text of its own between the documents;
  composition moving out of the shared startup template into per-agent code.

## More Information

Supersedes [ADR-007](ADR-007-agent-instruction-surface.md) whole, per Constitution Principle III's
whole-ADR supersession rule: ADR-007's system-prompt aspect is re-decided here, and its default-user-
prompt and message-scaffold aspect is re-decided, unchanged in substance, in
[ADR-054](ADR-054-default-user-prompt-and-message-scaffold.md). Nothing is inherited from ADR-007 by
reference.

Where the *effective* foundation document comes from — the build-distributed default and the optional
per-instance override — is a separate aspect, decided in
[ADR-055](ADR-055-foundation-document-resolution.md). This ADR fixes only which documents constitute a
system prompt and how they are composed.
