# Feature Specification: The Guarded Tool and Policy Surface Lint Needs

**Feature Branch**: `claude/issue-133-next-biwda4`

**Created**: 2026-08-22

**Status**: Draft

**Input**: User description: "The guarded tool and policy surface Lint needs (GitHub issue #159, milestone 1.0.0, replaces #64 and #150). Two limits of the current guarded surface bite at once. (1) Retrieval: Lint works through `list_files`, `read_file`, `write_file` only. `read_file` returns whole files and there is no search, so \"which pages mention X\" means reading the whole wiki — 633 pages / ~400k tokens, over the 200k context guard (#108). Handing the agent a shell is exactly what the guarded boundary exists to prevent. Wanted: a `search_files` tool (pattern + optional path prefix -> path, line number, matching line, capped) executed through `GuardedToolExecutor` against the same read policy so it can never surface a path the agent could not already read; a ranged `read_file` (offset/limit, or frontmatter-only), possibly the bigger win; and a `batch` form for read-only calls to spend fewer model turns, with writes excluded. (2) Write scope: the Lint policy is `frontmatter-only` and is shared between the survey run and remediation execution — `RemediationRunCoordinator` and `RemediationMessageTurnCoordinator` both pass `_paths.Lint.PolicyPath` — so an authorized remediation proposal that needs a body edit dead-ends after a human authorizes it, and the policy cannot be widened for execution without also widening the survey run. Decided 2026-08-21: an authorized remediation task gets write access to the content it was authorized to change. Open questions to carry into the spec as clarifications rather than resolve now: how wide the write grant is (TargetPath already travels on the execution request, so a grant scoped to the authorized page is available and much smaller than a wiki-wide one); whether search is regex or literal (regex makes frontmatter checks cheap for #42/#109 but needs complexity and timeout bounds); and the survey-vs-execution policy split. Sequencing is already decided by the owner: this lands ahead of #108, not inside it — #108's spec gets to assume these tools exist. Same layer, same ADRs — ADR-011 (tool contract) and ADR-016 (Lint's write scope) — so one spec, one ADR pass, one eval re-capture."

## Clarifications

### Session 2026-08-22

- Q: When a human authorizes a remediation task, how much of the wiki should that task's execution run be allowed to write? → A: Wiki-wide read-write for the duration of the authorized execution run (option C) — the grant is not scoped to the authorized target page.
- Q: Should the search tool match a literal string, or a regular expression? → A: Regular expression, with grep-compatible semantics — derived from the stated rule rather than chosen directly. The rule: the agent should have every capability it would have with a shell, confined to this wiki, so the guarded tools should mimic the shell tools it would otherwise reach for. `grep` takes a regex, so the search tool does too. Bounds (non-backtracking engine, timeout, result cap) are the resource limits any grep deployment has, not a reason to offer a weaker pattern language.
- Q: Should the Lint agent also be able to write the two reserved files — the wiki index and the activity log? → A: Yes (option A). Full access includes them; the exclusion in today's Lint policy goes away. Ingest already writes both, and the format rules for each (ADR-017 entry format, ADR-028 prepend ordering) are enforced independently of which agent writes them.
- Q: Should a Lint run be able to create new pages and delete existing ones, or only edit pages that already exist? → A: Full access — create, edit and delete (option C). The `frontmatter-only` limit was an implementation detail on the way to 1.0, not a designed boundary. Lint already acts on the wiki when it judges action is warranted; a remediation task is what it raises when it decides to leave an action to the user, not a permission it must obtain. Git history is the safety net for destructive change. Verbatim, as recorded: *"der lint agent hat vollen zugriff auf das wiki (der frontmatter only zugriff war nur ein implementierungsdetail auf dem weg zu Verision 1.0). der lint agent kann änderungen auch schon durchführen wenn er es für notwendig sieht. dinge, die er dem benutzer als \"aktion\" überlässt sollen als remediation tasks im ui angezeigt werden. […] we still have git as a major safety net"*
- Q: Should the survey scope and the execution scope live in two separate policy files, or in one file that declares both? → A: Neither — they are not separated at all. Lint and remediation are the same agent performing the same action; the human in the loop is a workflow step, not a permission boundary, and the agent has already judged what the wiki needs. One scope governs both modes. Combined with the answer above, this supersedes ADR-016's frontmatter-only decision rather than amending it: the unattended survey run holds the same wiki-wide write scope as an authorized remediation execution.
- Q: Should the four agent-judgment eval scenarios (SC-011–SC-014) stay as four gating criteria, or should the suite be reduced? → A: Reduced (option A). SC-011 is reworded to drop the "≥600-page" fixture requirement — the property under test is "wiki exceeds the context guard," satisfiable by lowering the eval budget rather than growing a corpus, which as drafted would have been ~70x the largest existing fixture in this repo. SC-012 and SC-014 are withdrawn as gating criteria (numbers retained, with a withdrawal note, so existing cross-references stay resolvable): SC-012 measured means rather than ends (SC-011 already tests the outcome); SC-014 is a measurement a metric already reports directly, not a judgment call. Decided earlier in this session, restated here through /speckit-clarify because a requirements change belongs in a clarify session, not a raw edit made in reaction to planning-phase feedback — the same rule this session now records for the process itself (see dev-experience.md / CLAUDE.md on stacked-PR delivery).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find the pages that mention something, without reading the wiki (Priority: P1)

An operator runs a Lint survey over a wiki that has grown past six hundred pages. The Lint
agent needs to know which pages mention a term, carry a given frontmatter field, or link
to a page it is checking. Today the only way to answer that question is to read every
page, which costs more content than the run is allowed to hold. The agent can instead ask
for matches: a pattern, optionally narrowed to a path prefix, answered with a capped list
of locations — file path, line number, and the matching line — and nothing the run's read
policy would not already have let it read.

**Why this priority**: This is the constraint that makes Lint unusable at real wiki size,
and every other retrieval improvement is a refinement of it. It is also the precondition
the "Lint at scale" work (#108) is allowed to assume, so it gates a downstream feature as
well as this one. Shipped alone it changes Lint from "cannot survey this wiki" to "can".

**Independent Test**: Run a Lint survey against a wiki fixture large enough to exceed the
context guard if read whole, with a term present on a known handful of pages. The run
completes, the findings reference those pages, and the recorded tool calls show matches
being requested rather than the whole wiki being read.

**Acceptance Scenarios**:

1. **Given** a wiki where three pages contain a term and six hundred do not, **When** the
   agent searches for that term, **Then** it receives the three locations with their line
   numbers and matching lines, and no content from the other pages.
2. **Given** a search whose path prefix points outside the run's allowed read scope,
   **When** the agent invokes it, **Then** the call is denied, the denial is recorded with
   a reason, the agent is told it was denied, and the run continues.
3. **Given** a pattern that matches far more locations than the result cap allows,
   **When** the agent invokes it, **Then** it receives the capped set together with an
   explicit signal that results were truncated, rather than an unbounded response.
4. **Given** a search over a scope that contains a file the read policy excludes, **When**
   the pattern matches inside that file, **Then** the location is absent from the results —
   search can never surface a path the agent could not already read.
5. **Given** a regular expression the engine will not accept — oversized, or requiring
   backtracking it does not support — **When** the agent issues it, **Then** it receives a
   denial naming the reason, and the run continues.

---

### User Story 2 - The agent can change page content, not only its frontmatter (Priority: P2)

Lint maintains the wiki. Today it may only edit frontmatter: the one policy it runs
under is `frontmatter-only`, and that policy governs both the survey run and the
execution of a remediation a human has authorized. So a fix that needs a body edit is
denied at the tool boundary — including after a human has already approved it, which is
where the loop visibly dead-ends. The agent must be able to write page content in both
modes.

**Why this priority**: It closes a loop that is currently broken end-to-end — a human
acts and nothing can happen. It also removes an inconsistency of principle: the wiki is
the agent's to maintain, and the agent already judged what the wiki needs when it raised
the finding. A human authorizing a remediation is a workflow step, not the moment the
agent acquires authority.

**Independent Test**: Have Lint write a page body during a survey run, and separately
have an authorized remediation execution do the same. Both succeed, both may create or
delete a page and write the index and activity log (FR-016a, FR-021a), and both are
refused only what FR-021 puts genuinely out of reach: anything outside the wiki content
root.

**Acceptance Scenarios**:

1. **Given** an authorized remediation task whose fix edits a page body, **When** the
   execution run writes that page, **Then** the write is allowed and the page body changes.
2. **Given** a Lint survey run with no human in the loop, **When** the agent writes a page
   body, **Then** the write is allowed on exactly the same terms as in an execution run —
   there is no narrower survey-only scope.
3. **Given** any Lint run in either mode, **When** it attempts to write anything outside the
   wiki content root, **Then** the write is denied and recorded with a reason, and the run
   continues with its allowed actions.
4. **Given** a Lint run that has just deleted a page, **When** it removes that page's entry from
   the index and records the deletion in the activity log, **Then** both writes are allowed and
   both are held to the same format rules any other agent's writes to those files are.
5. **Given** any Lint run, **When** it finishes, **Then** the task artifact records the
   identity (version and hash) of the policy that governed it.

---

### User Story 3 - Read only the part of a page that matters (Priority: P3)

Many of Lint's checks only need a page's frontmatter, or a few lines around a match it
just located. Reading the whole page to check one field spends context on content the
agent will not use. The agent can instead ask for a bounded slice of a page — its
frontmatter, or a range of lines — and receive exactly that.

**Why this priority**: It compounds the value of User Story 1 (a search result becomes
cheap to follow up on) and is the single biggest per-call saving for frontmatter checks,
but Lint can already function once search exists. It also carries a correctness edge — a
partial read must not be mistaken for having seen the page — that is cheaper to get right
once search is in place.

**Independent Test**: Ask for the frontmatter of a long page and confirm only the
frontmatter is returned; ask for a line range and confirm only those lines are returned;
confirm a page read only in part cannot then be overwritten.

**Acceptance Scenarios**:

1. **Given** a long page, **When** the agent requests only its frontmatter, **Then** it
   receives the frontmatter block and none of the body.
2. **Given** a page of known length, **When** the agent requests a line range beyond the
   end of the file, **Then** it receives an empty or partial result with an explicit
   end-of-file signal, not an error that ends the run.
3. **Given** a page the agent has read only in part, **When** it attempts to overwrite that
   page, **Then** the write is rejected on the grounds that the current content was never
   fully seen, and the rejection is recorded — a partial read never establishes the
   baseline that write coordination depends on.
4. **Given** an existing instruction file that reads whole pages, **When** it runs
   unchanged, **Then** it still receives whole pages — the slice is opt-in, not a change
   in default behavior.

---

### User Story 4 - Spend fewer turns on read-only work (Priority: P4)

Checking twenty pages' frontmatter costs twenty model turns, each with its own latency and
its own re-billed prompt. The agent can instead submit several read-only calls together
and receive their results together, spending one turn instead of many.

**Why this priority**: It is a cost and latency improvement over a surface that already
works once the first three stories land, and it is the piece most safely deferred. It is
also the piece where a mistake is most expensive — a write smuggled into a batch would
bypass the per-call reasoning the boundary depends on — so it is worth doing last, with
writes excluded by construction.

**Independent Test**: Submit a batch of read-only calls, some allowed and some denied, and
confirm each is evaluated and recorded individually and all results return together.
Submit a batch containing a write and confirm it is rejected.

**Acceptance Scenarios**:

1. **Given** a batch of read-only calls, **When** the agent submits it, **Then** all
   results return in one response and each call is evaluated against the policy on its own.
2. **Given** a batch in which one call is outside the read scope, **When** it is submitted,
   **Then** the allowed calls return their results, the denied call returns its denial, and
   the denial is recorded with a reason.
3. **Given** a batch containing a write call, **When** it is submitted, **Then** the batch
   is rejected without performing any of its calls, and the rejection is recorded.

---

### Edge Cases

- A search pattern that is computationally expensive against a large wiki — the run must
  stay bounded in time rather than hanging, and the agent must be told the search was cut
  short rather than that no matches exist.
- A search whose scope contains files that are not wiki content (binary, very large, or
  non-UTF-8) — these must not corrupt or blow up the result.
- A search path prefix using traversal or a symlink to reach outside the read scope — must
  collapse to a canonical path before the policy is evaluated, as existing path handling
  already does.
- A search matching inside a reserved file (the index and activity log) — the read policy
  decides, and search inherits that decision rather than making its own.
- A remediation task authorized without a target page recorded — under FR-016 this no longer
  changes what the run may write, but the recorded run must still show that no target was
  named, so the run's own record stays an accurate account of what it set out to do.
- The page an authorization named is deleted, renamed, or already changed between
  authorization and execution.
- A page is deleted while other pages still link to it — the harness does not decide what to
  do about the dangling links; whether to repoint, remove or leave them is the agent's
  judgment, and the deletion is not blocked on it.
- A page is created or deleted and the catalog or activity log is not updated to match —
  reconciling them is now within the agent's power (FR-016a) and therefore within its judgment;
  the harness does not enforce that the index agrees with the page set.
- Lint and Ingest write the index or the activity log at the same time — the existing
  cross-process coordination is now load-bearing for two more files than it was.
- A run deletes a page and then fails — the journal must restore it (FR-015a).
- A remediation execution and a survey run touching the same page at the same time — now
  that both hold the same write scope this is more likely, not less; the existing
  cross-process coordination decides the outcome and this feature adds no second mechanism.
- The policy is missing or unparseable — the run must fail before any wiki change, as it
  already does today.
- A ranged read followed by a write to the same page in the same run.
- A batch that contains another batch.

## Requirements *(mandatory)*

### Functional Requirements

**Retrieval**

- **FR-001**: The agent MUST be able to locate content by pattern across the wiki without
  reading whole pages, receiving for each match the file path, the line number, and the
  matching line.
- **FR-002**: A search MUST accept an optional path prefix that narrows where it looks.
- **FR-003**: Every search MUST be evaluated against the same read policy that governs
  reading, at the moment it is invoked, so that no result can name a path the agent was not
  already permitted to read.
- **FR-004**: A search that is denied MUST be recorded with a reason, reported to the agent
  as a denial, and MUST NOT end the run.
- **FR-005**: Search results MUST be capped, and a truncated result set MUST be explicitly
  signalled to the agent as truncated.
- **FR-006**: A search MUST be bounded in execution time; exceeding the bound MUST be
  reported to the agent as an incomplete search, never as "no matches" and never as a
  failed run.
- **FR-007**: The pattern MUST be a regular expression, with the semantics a `grep` user would
  expect — including a case-insensitive option. This is what makes structural frontmatter checks
  (#42, #109) expressible as one search rather than as reading every page.
- **FR-007a**: The regex MUST be evaluated by an engine that cannot backtrack catastrophically,
  and MUST carry a pattern-size bound. A pattern the engine cannot accept MUST come back as a
  recorded denial naming why, never as a hung run or a silent empty result.
- **FR-008**: The agent MUST be able to read a bounded slice of a page — its frontmatter, or
  a range of lines — instead of the whole page.
- **FR-009**: Reading a whole page MUST remain the default behavior when no slice is
  requested, so existing instruction files keep working unchanged.
- **FR-010**: A page read only in part MUST NOT establish the baseline that write
  coordination compares against; a write to a page whose current content was not fully read
  MUST be rejected and recorded.
- **FR-011**: The agent MUST be able to submit several read-only calls together and receive
  their results together.
- **FR-012**: A batch MUST reject any write call, and MUST perform none of its calls when it
  contains one.
- **FR-013**: Each call inside a batch MUST be evaluated against the policy individually,
  and each denial MUST be recorded individually with its own reason.

**Write scope**

- **FR-014**: One write scope MUST govern both the Lint survey run and remediation
  execution. The harness MUST NOT distinguish the two modes when deciding what a write may
  touch; a write allowed in one is allowed in the other, and a write denied in one is denied
  in the other.
- **FR-015**: That scope MUST give the agent full authority over wiki content: editing a
  page's frontmatter or body, creating a page, and deleting one. The `frontmatter-only`
  limit in force today is removed, not narrowed — it was scaffolding on the way to 1.0
  rather than a designed boundary.
- **FR-015a**: A deletion MUST be journaled with the deleted content so that a failed run
  restores it, on the same terms as the existing write journal restores an overwritten
  page. Deletion MUST NOT be the one action a rollback cannot undo.
- **FR-016**: The scope MUST cover the whole wiki content root — it is NOT narrowed to the
  page a remediation authorization named. The recorded target page remains a hint about
  intent, never a boundary the harness enforces, and a task authorized without one runs
  under the same scope as one that names a page.
- **FR-016a**: The scope MUST include the wiki index and the activity log. Lint's current
  exclusion of these two files is removed: an agent that can create and delete pages must be
  able to keep the catalog honest and record what it did. The only remaining boundary is the
  wiki content root itself — anything outside it stays denied.
- **FR-016b**: Admitting Lint to those two files MUST NOT relax the format rules that govern
  them. Whatever enforcement applies to the index's catalog entries and the activity log's
  prepend ordering applies identically regardless of which agent is writing.
- **FR-017**: There MUST be exactly one Lint policy artifact, shared by the survey run and
  both remediation execution paths. No per-mode policy file, mode selector, or scope overlay
  may be introduced: the absence of a split is the decision, not an unimplemented detail.
- **FR-018**: A write outside the grant in force MUST be denied and recorded with a reason,
  and the run MUST continue with its allowed actions.
- **FR-019**: Every run MUST record the identity (version and hash) of the policy that
  governed it, so a change to the shared scope is attributable to the runs that ran under it.
- **FR-020**: A missing or unparseable policy MUST fail the run before any wiki change is
  made.
- **FR-021**: Widening the write scope MUST NOT widen the read scope.
- **FR-021a**: Page creation and deletion MUST be permitted in both modes. Neither is gated
  on a human authorization: a remediation task is what the agent raises when it decides an
  action belongs to the user, not a permission it must acquire before acting.

**Shape of the tool surface**

- **FR-024**: The guarded tools MUST mimic the shell tools an agent would otherwise reach for,
  in both semantics and option naming: search behaves as `grep -rn` does, the ranged read as
  `sed -n 'X,Yp'` or `head` does, listing as `ls`/`find` does. Where a familiar tool's behavior
  and a bespoke design disagree, the familiar one wins.
- **FR-025**: The reason is that the agent arrives already fluent in these tools, so a
  shell-shaped surface needs no instruction-file explanation and invites fewer malformed calls.
  A capability that cannot be expressed in a shell-tool idiom is a signal to reconsider the
  capability, not to invent an idiom.
- **FR-026**: Mimicry stops at the boundary: these are guarded tools that behave like shell
  tools, never shell access. What a real shell would additionally offer — arbitrary commands,
  pipes to other binaries, anything outside the wiki content root — stays out of reach, and the
  guarded boundary is precisely the difference (FR-022).

**Boundary**

- **FR-022**: Every capability this feature adds MUST be exercised through the existing
  guarded tool boundary. No capability may reach the filesystem outside it, and none may be
  satisfied by giving the agent shell access.
- **FR-023**: What the new capabilities are used *for* — which pages to search, which slice
  to read, whether a body edit is the right fix — MUST remain the agent's judgment under its
  instruction files. The harness decides only whether a requested call is permitted.

### Key Entities

- **Search request**: a pattern, an optional path prefix, and the caps that bound it.
- **Search match**: a file path, a line number, and the matching line — the unit a search
  returns.
- **Read slice**: the bounded portion of a page a read returns — frontmatter, or a line
  range — distinct from a whole-page read in whether it can serve as a write baseline.
- **Read-only batch**: a group of read calls submitted and answered together; carries no
  authority of its own, only the sum of its members' individual evaluations.
- **Write scope**: the single write authority every Lint run holds, in either mode — full
  authority over everything inside the wiki content root: create, edit and delete pages, and
  write the index and activity log.
- **Policy identity**: the version and hash recorded on a run, identifying which revision of
  the shared scope governed it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic harness guarantees**

- **SC-001**: 100% of search results name paths the run's read policy already allows; no
  result surfaces a path a read would have been denied.
- **SC-002**: 100% of denied searches, denied slice reads, denied batch members, and denied
  writes are recorded with a reason and leave the run running.
- **SC-003**: 100% of searches return within their time bound and within their result cap,
  and 100% of truncated or time-bounded results are explicitly marked as incomplete.
- **SC-004**: For 100% of write attempts, the decision is identical in a survey run and in a
  remediation execution run given the same path and the same content — mode never changes the
  outcome.
- **SC-005**: 100% of writes attempted outside the wiki content root are denied and recorded,
  in either mode.
- **SC-005b**: 100% of Lint writes to the index and the activity log are held to the same format
  rules as any other agent's writes to those files.
- **SC-005a**: 100% of pages deleted by a run that subsequently fails are restored by the
  rollback journal.
- **SC-006**: 100% of remediation tasks whose proposal targets page content run under a scope
  that permits that change — no task is blocked by a frontmatter-only limit.
- **SC-007**: 100% of batches containing a write are rejected without performing any of
  their calls.
- **SC-008**: 100% of write attempts against a page whose current content was not fully read
  in the same run are rejected.
- **SC-009**: 100% of runs record the policy identity that governed them.
- **SC-010**: 100% of runs whose governing policy is missing or unparseable fail before any
  wiki file changes.

**Agent-judgment evaluation thresholds**

- **SC-011**: On a wiki larger than the run's context guard, ≥ 90% of sampled Lint survey runs
  complete their survey while the total page content they read stays under that guard — i.e.
  the agent narrows with search and slices instead of reading the wiki.
- **SC-013**: ≥ 90% of sampled authorized body-edit remediations produce a page change that
  a reviewer scores as addressing the authorized proposal.

**Withdrawn 2026-08-22, numbers retained so references stay resolvable:**

- **SC-012** *(withdrawn)*: "≥ 90% of sampled searches are scoped rather than unbounded
  sweeps." Measured means rather than ends. SC-011 already tests the outcome, and pinning
  *how* the agent retrieves would ossify agent behaviour for no added assurance.
- **SC-014** *(withdrawn as a criterion)*: "median content tokens read drops ≥ 50% against the
  pre-feature baseline." This is a measurement, not a judgment: `wiki.read.invocations_total`
  by read shape reports it directly. It is expected to hold and the before/after numbers are
  recorded in the implementation PR, but it gates nothing — a criterion the feature must *meet*
  is different from an effect it should *have*.

## Assumptions

- The existing cross-process write lock and compare-and-swap coordination is reused
  unchanged; this feature adds no second coordination mechanism.
- The index and activity log are no longer excluded for Lint. Their exclusion for any other
  agent is untouched by this feature — this widens Lint's scope, not the policy model's defaults.
- Search reads the same content root that reading already reaches, and inherits the run's
  read policy rather than declaring a scope of its own.
- Fail-closed handling of a missing or unparseable policy is retained as-is. No new policy
  artifact is introduced — the existing single Lint policy is the one that changes.
- The three existing tool names keep their current shapes; the slice parameters are additive
  and optional, so instruction files that do not use them behave exactly as today.
- The new retrieval capabilities are offered per agent through the existing per-agent tool
  registries. Lint gets them because it needs them; whether Ingest and Query are offered the
  same capabilities is a registry decision made when each of those agents needs it, and this
  feature does not change their policies.
- The human authorization step itself — who may authorize, and how — is unchanged as a
  workflow. What changes is that it is no longer also a permission boundary: it gates whether
  a proposed remediation runs, not what a run is permitted to write.
- No new infrastructure, no new external system, and no shell or process-execution capability
  for the agent. The tools imitate shell tools; they do not provide one. Confining the agent to
  this wiki is the whole reason the imitation exists rather than the real thing.
- Git history is the standing safety net for destructive change: the wiki is a git-tracked
  working tree, so a bad edit or deletion is recoverable outside the harness. This is why the
  scope can be full without the harness adding a confirmation or soft-delete mechanism of its
  own — but it is a recovery path a human takes, not one the harness automates.
- Remediation tasks remain the agent's way of handing an action to the user. Removing the
  permission dimension does not remove the workflow: what changes is why a task exists (the
  agent chose to defer) rather than what it unlocks (authority the agent lacked).
- One recorded-eval re-capture covers the instruction-file changes this feature requires,
  per the existing recorded-replay eval approach. Lint's instruction file will need to carry
  the judgment that the removed policy limit used to enforce mechanically — when a body edit
  is warranted at all — which is agent-judgment coverage (SC-013), not a harness assertion.
- Sequencing is settled: this feature lands ahead of the "Lint at scale" work (#108), whose
  own spec may assume these capabilities exist.

## Dependencies

- **ADR-006** — the guarded tool-use loop and deny-by-default policy model: this feature adds
  tools and a scope to that boundary and must not create a second one.
- **ADR-011** — the shared agent runtime and tool-registry contract: new tool definitions and
  the registry's unknown-tool rejection govern how a capability is offered per agent.
- **ADR-015** — cross-process write coordination and compare-and-swap: reused unchanged; the
  partial-read rule (FR-010) exists to protect its baseline.
- **ADR-016** — Lint's frontmatter-only write scope: this feature **supersedes** it. The
  clarified decision removes the frontmatter-only limit in both modes rather than narrowing
  or amending it, so a superseding ADR with bidirectional status links and a `docs/adr/index.md`
  update is expected at `/speckit-plan` — not an amendment. Note that ADR-016's own
  `frontmatter-only` write mode may still be worth keeping in the policy model even when no
  Lint policy uses it.
- **ADR-018** — remediation authorization and execution: authorization gates whether a
  proposed remediation *runs*, not what write authority the run holds — the run's scope is
  the same one every Lint run holds (FR-014). This feature does not derive a scoped write
  grant from the authorization; it is worth stating plainly here so `/speckit-plan` does not
  reintroduce grant-scoping.
- Replaces issues **#64** (remediation write access) and **#150** (guarded search and batching).
