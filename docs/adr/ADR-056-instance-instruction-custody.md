---
status: proposed
---

# ADR-056: One Named Custodian May Persist an Instruction Document It Received Whole, and Nothing May Author One

> **Status notes** (informational, no status change):
> - Extends [ADR-006](ADR-006-agent-tool-loop-guarded-boundary.md): the same authorship-versus-custody
>   split the guarded tool boundary already applies to wiki content, applied to one instruction
>   document.
> - Related: [ADR-053](ADR-053-agent-system-prompt-composition.md), which owns the Boundary Rule this
>   ADR widens.

## Context and Problem Statement

An operator must be able to say what kind of wiki their instance maintains, and the resulting statement
must end up on disk where the agents read it. Somebody has to write that file.

Today nothing in production code may: the structural rule enforced by
`InstructionAuthorshipBoundaryRuleTests` flags any production type outside the hub's path-composition
namespace that combines an agent-instruction filename literal with a file-write API.
[ADR-043](ADR-043-build-distributed-agent-artifacts.md) put it there deliberately, rejecting
"instruction files written out by the hub" because it **makes the hub the author of instruction
content** — the Principle V violation the whole harness/agent split exists to prevent.

The identity wizard needs the file written by the system itself: the operator drives it through the
system's own interface, and a wizard that can only tell an operator to go write a file by hand inside a
container volume is not a wizard. So either the rule bends, or the feature does not exist. The question
is which distinction the rule was actually protecting, and whether it can be stated precisely enough to
enforce.

## Decision Drivers

- Principle V's real content: judgment about what the wiki is and how it works must be exercised by an
  agent under instruction files, never produced by backend code.
- The harness already has a precise answer to the same question one layer down: agents author wiki
  pages, the guarded tool layer persists the bytes it is handed, and nobody mistakes the guard for the
  author.
- A rule that is relaxed must stay enforceable — a Boundary Rule with a Red/Green probe, not a comment.
- The relaxation must be the smallest that makes the feature possible: one component, one document.

## Considered Options

1. **Authorship versus custody**: one named custodian may write the instance document, and only bytes it
   received whole; nothing anywhere may produce instruction text.
2. **Leave the rule untouched**: the Hub reports what an operator must write by hand into the data
   volume.
3. **Drop the rule for instruction files generally**, trusting review to catch authorship.
4. **Let the agent write it through the guarded tool layer**, treating the foundation document as
   wiki-adjacent content.

## Decision Outcome

Chosen option: **Option 1.**

- **Nothing in production code may author instruction content.** Composing, templating, interpolating,
  summarizing, defaulting, merging or otherwise producing the text of an instruction document is
  forbidden everywhere, without exception. This is the rule ADR-043 was protecting and it is not
  weakened here.
- **Exactly one named custodian component may persist an instruction document**, and only under these
  constraints:
  - it writes **only bytes it received whole** from outside the system — it never constructs, edits or
    merges content;
  - it writes **only the instance foundation document**, at the single location the feature's own
    artifacts fix (`specs/029-shared-foundation-prompt/contracts/foundation-document.md`) — this ADR
    binds the custodian to *one* document at *one* location, not to which location that is;
  - its validation is limited to what custody requires — the document is readable and not effectively
    empty — never to what the content says;
  - it refuses to replace an existing document without an explicit operator decision, so custody never
    silently destroys what it holds.
- **Boundary Rule** (Principle III, existing rule widened, owned by ADR-053): the instruction-authorship
  structural test gains `foundation-prompt.md` in its literal set and the custodian's namespace in its
  allow-list. Both halves keep the Red/Green probe: a deliberate violation outside the allowed namespace
  must fail the test, and the probe must cover the new literal specifically, not only the pre-existing
  ones.
- **The custodian is not a decision-maker.** Which document an instance ends up with is decided by the
  operator (default or specialised) and drafted by an agent session outside the system. The custodian's
  entire contribution is that the bytes reach the right file intact.

### Consequences

- Good, because the distinction the rule always meant — author versus custodian — is now stated
  explicitly and enforced structurally, instead of being approximated by "no production code may write
  these filenames at all".
- Good, because it reuses the split the harness already lives by at the guarded tool boundary, rather
  than inventing a second, differently-shaped exception.
- Bad, because the structural rule now has an allow-list with two entries instead of one, and every
  future entry is a decision that must go through this ADR's supersession. Accepted: that friction is
  the point — an allow-list that grows quietly is not a boundary.
- Bad, because "bytes received whole" is a property a structural test cannot fully prove; the IL-level
  rule proves only *which component* may write, and behavioral tests prove the bytes are unmodified.
  Accepted, and stated here so nobody mistakes the structural test for the whole guarantee.
- Neutral, because the operator can still hand-edit the document directly; custody is a convenience for
  producing one, never the only sanctioned way to have one.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a second user-facing surface driving the same custodian
  (a web UI calling the same code path); additional custody-level validation that still says nothing
  about content; reporting on what the custodian holds.
- **Invalidations (would require full supersession):** a second component added to the allow-list; the
  custodian gaining the ability to produce, template or merge instruction text; custody extended to a
  second instruction document; the custodian replacing an existing document without an explicit
  operator decision; instruction documents becoming writable through the agent-facing guarded tools.

## More Information

The location this custodian writes to, and how the effective document is resolved from it, are feature
behaviour rather than an architectural decision, and live in the feature's own artifacts
(`specs/029-shared-foundation-prompt/plan.md`, `data-model.md`,
`contracts/foundation-document.md`), with the alternatives weighed in that feature's `research.md`
R1/R6. The Boundary Rule's literal set and allow-list live with the instruction surface in
[ADR-053](ADR-053-agent-system-prompt-composition.md); this ADR decides *that* an allow-list entry
exists and on what terms, not the mechanics of the test.

The reasoning that led here — why ADR-043's rejection of "hub writes instruction files" does not settle
this case — is recorded in `specs/029-shared-foundation-prompt/research.md` R7.
