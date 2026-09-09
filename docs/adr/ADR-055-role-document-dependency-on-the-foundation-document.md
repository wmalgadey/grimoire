---
status: proposed
---

# ADR-055: A Role Document Degrades When the Foundation Document Is Silent, and the Harness Never Looks

> **Status notes** (informational, no status change):
> - Extends [ADR-053](ADR-053-agent-system-prompt-composition.md): ADR-053 created the two document
>   layers and decided how they are composed; this ADR decides what one layer may assume of the
>   other. ADR-053's own decision is unchanged.
> - Related: [ADR-031](ADR-031-lint-full-wiki-write-scope.md), whose grant of full wiki authority to
>   Lint is what makes a role document capable of depending on shared definitions in the first
>   place; neither ADR changes the other.

## Context and Problem Statement

[ADR-053](ADR-053-agent-system-prompt-composition.md) split an agent's system prompt into a shared
**foundation document** and a per-agent **role document**, and decided the mechanics of composing
them: fixed order, verbatim, fail-closed when a document is missing or effectively empty, per-document
SHA-256. That split also created two layers with different owners — the foundation document is what a
deployment replaces to maintain a different kind of wiki, while role documents are product-owned.

ADR-053 did not decide what happens when a foundation document loads perfectly well and is simply
**silent** about something a role document depends on. That is not a hypothetical: a role document
that reads or writes a frontmatter field, applies a scoring convention, or follows a folder layout is
depending on a definition that lives in the foundation document, and an instance is free to author a
foundation document that never mentions it. Every one of ADR-053's checks passes — the file exists, is
readable, is non-empty, hashes fine — and the role document is left assuming something that was never
said.

Two failure modes are available if this is left undecided, and both are bad in ways that are hard to
notice. The agent may proceed as though the definition existed, inventing one silently and producing
output the operator has no reason to distrust. Or the harness may grow a check for it — reading the
composed instruction text, looking for the definition, and refusing to dispatch — which would make the
harness an interpreter of instruction content and put it squarely in breach of Constitution Principle
V.

The question generalises well beyond any one field, and it will be asked again: issue #224
(instance-owned role documents, deferred to 2.0.0) would put *both* layers in instance hands, at which
point "what may one document assume of another" is the whole of the contract between them.

## Decision Drivers

- Constitution Principle V: the harness "accepts and executes [instruction files] without
  special-casing or reinterpreting their content". A content check is exactly the special-casing that
  forbids.
- An instance that authors its own foundation document must be able to omit things without bricking
  the agents that read it — otherwise the wiki-identity capability ADR-053 enables is only usable by
  someone who has read all three role documents first.
- A capability that silently disappears is worse than one that visibly degrades. The operator loop
  (Principle V) only closes if the operator can see what was lost.
- The answer must not depend on which fields exist today, or it will be re-litigated with every
  feature that adds one.

## Considered Options

1. **The role document degrades and says so**: the agent carries out the parts of its role whose
   inputs are defined, skips the parts whose inputs are not, and names each skipped part and the
   reason in its own output. The harness is not involved.
2. **The role document fails closed**: the agent refuses the run and reports why.
3. **A minimal definition set is product-owned**: some definitions hold regardless of what the
   foundation document says, carved out of the instance's authority.
4. **The harness validates the foundation document** against a declared set of definitions each role
   document requires, and refuses to dispatch when one is missing.

## Decision Outcome

Chosen option: **Option 1**, because it is the only one that keeps the harness out of instruction
content while still making the loss visible to the operator.

Option 4 is rejected outright: it requires the harness to parse and interpret instruction text, which
Principle V forbids, and it would need a machine-readable declaration of requirements that would
immediately become a second source of truth able to drift from the prose. Option 2 is available only
as an instruction to the agent — never as a harness check — and even then costs an instance every
capability of a role over one missing definition. Option 3 guarantees the role keeps working, but
splits ownership of the foundation document so that an operator authoring one cannot tell which parts
are theirs; it also does not generalise to #224, where a product-owned carve-out would be needed in
two layers rather than one.

### R1 — Degradation is the role document's own behaviour (Feature-Scoped Invariant)

A role document MAY depend on a definition stated in the foundation document. Where that definition is
absent, the agent MUST carry out every part of its role whose inputs are defined, MUST skip only the
parts whose inputs are not, and MUST name each skipped part and the reason in its own output.

Tagged a **Feature-Scoped Invariant**, not a Boundary Rule: it describes what an agent does with the
text it was given, not a dependency direction between packages or namespaces. Per Constitution
Principle III it therefore takes a classicist, state-based test of the observable behaviour — a run
against a foundation document lacking a definition completes rather than errors — and MUST NOT be given
a reflection- or IL-based structural test. The half that is agent judgment (that the report explains
*which* parts were skipped and *why*) is agent behaviour under Principle II and is verified by the
user-reported correction loop, not by a deterministic assertion.

### R2 — The harness never inspects instruction content to detect the gap (Boundary Rule)

No production type may read, parse, search, or branch on the *content* of an instruction document in
order to decide whether to dispatch a run, which parts of a run to execute, or how to degrade. The
harness's dealings with instruction documents remain exactly what ADR-053 decided: resolve a path, read
bytes, verify non-emptiness, hash, compose in the fixed order, hand over.

Tagged a **Boundary Rule**: it is a dependency direction — instruction content flows out of the harness
to the agent and never back into a harness decision. It extends the existing structural rule ADR-053
states ("no production type may author instruction content") from *authorship* to *inspection*, and
keeps that rule's Red/Green probe, extended to cover a deliberate content-inspecting call site.

Verifying non-emptiness is not inspection: it is a property of the bytes, not of what they say.

## Consequences

- Good, because an instance can author a foundation document without knowing what any role document
  depends on, and the worst outcome is a named, visible reduction in what that role does.
- Good, because it generalises to #224 unchanged: a role document states what it needs and degrades
  when it is absent, whichever layer owns which document.
- Good, because it keeps a single owner for the foundation document. Nothing in it is secretly
  product-owned.
- Bad, because a degraded run is less obviously wrong than a failed one — an operator who does not read
  the report may not notice a capability is missing. Mitigated by R1's requirement that the skip and
  its reason be named in the agent's own output, on a surface the operator already consults, rather
  than logged somewhere they do not.
- Bad, because "which parts of my role are affected" becomes a judgment the agent makes at runtime
  rather than a fact the system can enumerate. Accepted: enumerating it is precisely the machine-
  readable requirements declaration option 4 was rejected for.
- Neutral, because nothing about composition, loading, fail-closed behaviour or hashing changes;
  ADR-053 carries all of it unchanged.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new role document depending on a foundation-document
  definition; a role document adding or removing a dependency; a new agent type; role documents
  becoming instance-owned (#224), which changes who authors the documents but not what one may assume
  of another; growth in what a degraded run reports, so long as the report stays agent-authored.
- **Invalidations (would require full supersession):** the harness gaining any check on instruction
  *content*, however narrow, including a machine-readable manifest of required definitions; a role
  document being permitted to fail the run rather than degrade; a definition set being carved out as
  product-owned regardless of the foundation document; degradation moving out of the agent's own
  output into a harness-emitted signal.

## More Information

Extends [ADR-053](ADR-053-agent-system-prompt-composition.md), which decided the two-document
composition this ADR presupposes; ADR-053's decision is used, not narrowed. Read alongside
[ADR-031](ADR-031-lint-full-wiki-write-scope.md) for the authority a role document exercises over wiki
content, and [ADR-030](ADR-030-guarded-retrieval-tool-surface.md) for the reading surface an agent uses
when acting on what it was given.

First applied by feature 030 (`specs/030-prompt-conformance/`), whose FR-010 and FR-010a are this ADR's
R1 and R2 in that feature's terms. The ADR is deliberately stated without reference to which fields
that feature happens to need, per Constitution Principle III's prohibition on feature content in an
ADR.
