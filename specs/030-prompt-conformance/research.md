# Phase 0 Research — Feature 030, Prompt Conformance

Five questions had to be settled before the design could be written. Three were left to plan level
by the spec on purpose; one is a constitutional question about whether this feature needs an ADR (it
does not, and a drafted one was withdrawn); and one is a defect in the spec itself that this research
found, refused to fix from here, and routed to `/speckit-clarify` — where it was resolved by
removing the requirement rather than tightening it (R5).

---

## R1 — What thresholds make the confidence convention usable for the agent?

**Decision**: `high` ≥ 1 · `medium` −1 … 0 · `low` ≤ −2, and **the same cut points apply to lint's
extended scale**. Lint's two extra signals move a page's total; they do not move the boundaries.

**Status of this decision after the 2026-09-10 clarification**: it is a **judgment about what makes
the aid usable, not a criterion anything verifies**. FR-005 no longer states a MUST-level property
about the convention's shape — confidence scoring is a means for the agent, and the agent is what
evaluates it. The numbers below are therefore a recommendation carried into the instruction document,
and the reachability analysis is the reasoning behind them rather than a test they pass. If they
later read wrong in practice, the correction path is an instruction-file edit through the operator
loop, not a spec gate.

**Rationale.** The shared convention after D2 carries five observable signals — two positive
(three or more independent sources; a book or official documentation) and three negative (a
social/blog source; an explicit contradiction marker; a stale source on a fast-moving topic) — so
its attainable range is `−3 … +2`. Lint's documented extension adds `inbound_links ≥ 3 → +1` and
orphan → `−1`, giving it `−4 … +3`.

Checking every band against both ranges with one threshold table:

| Band | Cut | Reachable on the shared scale (`−3 … +2`) | Reachable on lint's scale (`−4 … +3`) |
|------|-----|-------------------------------------------|----------------------------------------|
| `high` | ≥ 1 | +1, +2 | +1, +2, +3 |
| `medium` | −1 … 0 | −1, 0 | −1, 0 |
| `low` | ≤ −2 | −2, −3 | −2, −3, −4 |

Every band is reachable on both scales by more than one combination, which is what the old table
failed to do. It also reads correctly in prose: a page backed by a book or by three-plus independent
sources, with nothing counting against it, is `high`; two things counting against it make it `low`.
Under lint's extension, orphanhood can pull a well-sourced page down out of `high`, and strong
interlinking can lift a page out of `medium` — which is precisely the coupling issue #110 says was
lost.

**Alternatives considered.**

- *Keep `≥ 2` for `high`.* Rejected — on a `−3 … +2` scale that means `high` requires a **perfect
  score**: both positives and no negatives. That is the defect #110 reports, not a fix for it.
- *`high ≥ 1` · `medium 0` · `low < 0`.* Rejected — it trades one collapsed band for another,
  leaving `medium` reachable only at exactly 0.
- *A second, separate threshold table for lint.* Rejected as unnecessary complexity. One table works
  for both ranges, and FR-006's requirement that the extension "state the thresholds that apply to
  the wider range" is satisfied by stating explicitly that they are the same — which is more useful
  to a reader than a duplicated table that happens to hold identical numbers.

---

## R2 — Does this feature require a new ADR?

**Decision**: **No.** Every change in the feature is an **extension** of an existing ADR and changes
no ADR's status. A new ADR was drafted here (ADR-055, on what a role document may assume of the
foundation document) and then withdrawn; both the draft and the withdrawal are recorded below,
because the reasoning that killed it is reusable.

**Rationale.** Principle III's invalidation test asks whether honouring the new requirement would
reverse, narrow or contradict what an ADR actually decided.

- *Content edits to either document* — ADR-053's own Change Triggers name "growth or rewriting of
  either document's content" and "moving text between the two documents" as extensions. Explicitly
  not an invalidation.
- *Lint writing `last_reviewed`* — ADR-031 already grants lint `read-write` on the whole content
  root with no exclusions. No new capability, so nothing to re-decide.
- *Re-capturing recordings* — ADR-012 designed this gate; it firing is the ADR working, not
  changing.

**Why the drafted ADR-055 was withdrawn.** D3 asks what a role document may assume when the
foundation document loads fine but is *silent* about something it depends on. That question is real,
and ADR-053 does not answer it — but "not answered by an ADR" is not the same as "needs an ADR".
Three things are wrong with recording it as one:

1. **Its substance is feature content, not a boundary.** The rule "the agent does the parts of its
   role whose inputs are defined and names what it skipped" is a statement about agent behaviour
   under instruction files. Principle III forbids an ADR from fixing behaviour: that belongs in
   `spec.md`, where `/speckit-clarify` already put it as FR-010.
2. **Its one boundary-shaped half is already decided, above the ADR layer.** "The harness never
   inspects instruction content in order to decide anything" is Constitution Principle V verbatim —
   the harness "accepts and executes [instruction files] without special-casing or reinterpreting
   their content". An ADR restating a constitutional rule creates a second place to look for one
   rule, and invites the reading that the rule holds *because* the ADR says so.
3. **The failure it guards against is visible, not silent — and the case it names is the very
   defect this feature removes.** A first draft of this section claimed "no instance today runs a
   foundation document silent on these fields". **That was false, and checking it is what makes the
   rest of the argument honest**: the shipped
   `backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md` mentions neither
   `inbound_links` nor `last_reviewed`, while the lint role document references them ten times.
   Silence is the *status quo*, and it is exactly what issue #109 reports.

   That strengthens the withdrawal rather than reversing it. The condition has held for the whole
   life of the two-document split, and what it produced was a documented defect — visible in the
   wiki, in the findings report, and eventually in an issue — not a silent corruption that a
   boundary decision would have caught. Nobody needed a detector, and there was no false positive
   to guard against. After this feature the shipped foundation document states both fields, so the
   only remaining case is an **operator-authored replacement** that omits them; that one is
   genuinely hypothetical, and Principle I is explicit that structural boundaries are earned, not
   assumed upfront.

**What this changes downstream**: nothing in `spec.md`. FR-010 and FR-010a already state the
behaviour and the prohibition; they simply are not backed by an ADR, and do not need to be. Phase 0
of `tasks.md` therefore states "no Boundary Rule introduced by this feature" rather than writing a
structural test.

---

## R3 — What is the qualifying "review" act, given the tools lint actually has?

**Decision**: D5 resolved this to "the run produced a substantive finding or a remediation
proposal". This research confirms the resolution is implementable against the existing tool surface
and identifies one constraint the documents must respect.

**Constraint found (ADR-018).** A remediation *proposal* is produced during an ordinary lint run; a
remediation *execution* is a separate, human-authorised run. FR-002b must key on the **proposal**.
Keying on the execution would make the review date depend on a human acting, so a wiki nobody
remediates would never accumulate review dates at all — reintroducing the defect the field exists to
fix.

**Second constraint (ADR-030).** Because the qualifying act is "produced a finding", it does not
depend on *which* retrieval tool lint used. That is a genuine advantage of option B over the
rejected option A: option A would have coupled the review date to `read_file(path)` versus
`read_file(frontmatter_only)`, tying a wiki-content rule to ADR-030's tool surface, so a later change
to that surface would silently change what "reviewed" means.

---

## R4 — What does this feature need to instrument?

**Decision**: nothing. No metric, no log event, no span.

**Rationale.** Instrumentation follows code paths, and this feature adds none. Every behaviour it
changes happens inside agent judgment, whose externally visible traces — the wiki pages themselves,
the task artifact, the lint findings report — already exist and already reach the operator. The
existing signals covering these runs (`hub.ingest_submissions_total`,
`hub.lint_lifecycle_updates_total`, `lint.tool_calls_total`,
`wiki.identity.foundation_resolved_total`) are unmodified.

Inventing signals to fill the Observability tables would produce exactly the failure Principle V
names: a signal emitted but consumable nowhere the user looks. The honest output is an empty table
plus a named surface per criterion, which is what `plan.md` carries.

---

## R5 — A defect in FR-005, found while computing R1, and since resolved by clarification

**Finding (2026-09-09)**: FR-005 said the convention "MUST be internally coherent: every band its
thresholds define MUST be reachable from the signals it lists." **The status quo already satisfied
that.** On the `−3 … +2` range with `high ≥ 2`, the `high` band *is* reachable — at exactly `+2`. So
FR-005, read literally, did not require the fix issue #110 asks for.

The actual defect #110 reports is narrower and sharper: `high` requires a **perfect score** — both
positive signals firing and no negative one. "Reachable" and "reasonably reachable" are different
properties, and the requirement as written only demanded the first.

**Resolution (2026-09-10)**: routed through `/speckit-clarify`, and the answer rejected the premise
this research had assumed. Three options were offered, all of which would have *tightened* FR-005
(reachable by more than one combination; no band may require a perfect score; move the property to a
success criterion). The decision was **none of them: no MUST criteria for linting or for confidence
at all.** The convention is a means for the agent to exercise judgment; nothing here needs to be
deterministically verified or guaranteed.

That closes the defect by **removing** the criterion rather than sharpening it, and it is the better
resolution for a reason this research had not weighed: every option on the table would have written a
checkable property about the shape of an LLM's judgment aid, which is the same category error as
asserting a deterministic contract over agent behaviour. The exposure the finding named — that a
future change could weaken the thresholds without violating FR-005 — is real and is now accepted
deliberately: it surfaces as an operator reading confidence scores that look wrong (Constitution
Principle II's user-reported correction loop), not as a red gate.

**What still happens in this feature**: the thresholds change. `high ≥ 2` on a `−3 … +2` range
demands a perfect score, which is #110's actual complaint, and R1's numbers replace it. That change
is now a recorded judgment in these planning artifacts rather than a requirement `spec.md` enforces.

**A note on process, since the first version of this section got it half right.** The finding
correctly refused to edit `spec.md` from the plan layer and routed the question to
`/speckit-clarify`. It was wrong only in assuming the fix had to be a *stronger* requirement — it
proposed the "more than one combination" wording as the recommended answer. Asking the question was
what surfaced the better option; had this research patched FR-005 itself, the spec would have gained
a criterion the user did not want.
