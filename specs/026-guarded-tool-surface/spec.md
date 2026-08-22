# Feature Specification: The Guarded Tool and Policy Surface Lint Needs

**Feature Branch**: `claude/issue-133-next-biwda4`

**Created**: 2026-08-22

**Status**: Draft

**Input**: User description: "The guarded tool and policy surface Lint needs (GitHub issue #159, milestone 1.0.0, replaces #64 and #150). Two limits of the current guarded surface bite at once. (1) Retrieval: Lint works through `list_files`, `read_file`, `write_file` only. `read_file` returns whole files and there is no search, so \"which pages mention X\" means reading the whole wiki — 633 pages / ~400k tokens, over the 200k context guard (#108). Handing the agent a shell is exactly what the guarded boundary exists to prevent. Wanted: a `search_files` tool (pattern + optional path prefix -> path, line number, matching line, capped) executed through `GuardedToolExecutor` against the same read policy so it can never surface a path the agent could not already read; a ranged `read_file` (offset/limit, or frontmatter-only), possibly the bigger win; and a `batch` form for read-only calls to spend fewer model turns, with writes excluded. (2) Write scope: the Lint policy is `frontmatter-only` and is shared between the survey run and remediation execution — `RemediationRunCoordinator` and `RemediationMessageTurnCoordinator` both pass `_paths.Lint.PolicyPath` — so an authorized remediation proposal that needs a body edit dead-ends after a human authorizes it, and the policy cannot be widened for execution without also widening the survey run. Decided 2026-08-21: an authorized remediation task gets write access to the content it was authorized to change. Open questions to carry into the spec as clarifications rather than resolve now: how wide the write grant is (TargetPath already travels on the execution request, so a grant scoped to the authorized page is available and much smaller than a wiki-wide one); whether search is regex or literal (regex makes frontmatter checks cheap for #42/#109 but needs complexity and timeout bounds); and the survey-vs-execution policy split. Sequencing is already decided by the owner: this lands ahead of #108, not inside it — #108's spec gets to assume these tools exist. Same layer, same ADRs — ADR-011 (tool contract) and ADR-016 (Lint's write scope) — so one spec, one ADR pass, one eval re-capture."

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

---

### User Story 2 - An authorized fix can change what it was authorized to change (Priority: P2)

A Lint survey proposes a remediation whose fix requires editing a page's body, not just
its frontmatter. A human reviews the proposal and authorizes it. Today the execution run
inherits the survey run's frontmatter-only write scope, so the authorized fix is denied at
the tool boundary and the task dead-ends after the human has already approved it. The
human's authorization must instead carry write access to the content it authorized —
while the survey run itself keeps its frontmatter-only limit exactly as it is.

**Why this priority**: It closes a loop that is currently broken end-to-end — a human acts
and nothing can happen — and the governing decision was already made on 2026-08-21, so
only its scope is open. It is contained: it changes what an authorized execution may write
and nothing about what the survey may write.

**Independent Test**: Author a remediation proposal that requires a body edit, authorize
it, and let it execute. The page body changes, and the same body edit attempted during a
survey run is still denied and recorded.

**Acceptance Scenarios**:

1. **Given** an authorized remediation task whose fix edits a page body, **When** the
   execution run writes that page, **Then** the write is allowed and the page body changes.
2. **Given** a Lint survey run (not a remediation execution), **When** the agent attempts a
   write that changes a page body, **Then** the write is denied and recorded with a reason —
   unchanged from today.
3. **Given** an authorized remediation execution, **When** it attempts a body write to a
   page outside the grant its authorization carries, **Then** the write is denied and
   recorded with a reason, and the run continues with its allowed actions.
4. **Given** an authorized remediation execution, **When** its run finishes, **Then** the
   task artifact records the identity (version and hash) of the policy that actually
   governed it, distinguishable from the survey policy.

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
- A remediation task authorized without a target page recorded — the authorization carries
  no page to scope a grant to. See the clarification on grant width below.
- The page an authorization named is deleted, renamed, or already changed between
  authorization and execution.
- A remediation execution and a survey run touching the same page at the same time — the
  existing cross-process coordination decides the outcome; this feature adds no second
  coordination mechanism.
- The execution-scope policy is missing or unparseable — the run must fail before any wiki
  change, as an unparseable policy already does today.
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
- **FR-007**: The pattern language MUST be [NEEDS CLARIFICATION: literal substring matching,
  or a bounded regular-expression syntax? Regular expressions make frontmatter-field checks
  (#42, #109) cheap to express but require an explicit complexity and timeout bound; literal
  matching needs neither but cannot express "pages whose `inbound_links` field is absent".]
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

- **FR-014**: A Lint survey run MUST keep its frontmatter-only write scope: a write that
  changes a page body during a survey run MUST be denied and recorded with a reason.
- **FR-015**: A remediation execution run acting on a human-authorized task MUST be able to
  write the content it was authorized to change, including page bodies.
- **FR-016**: The scope of that write grant MUST be [NEEDS CLARIFICATION: limited to the
  single page the authorization named, or wiki-wide for the duration of the execution run?
  The authorized target page already travels on the execution request, so a per-page grant
  is available; it is also optional and agent-authored, so a task authorized without a
  target page needs a defined outcome — deny body writes and fall back to frontmatter-only,
  or refuse to dispatch the execution at all.]
- **FR-017**: The survey scope and the execution scope MUST be independently changeable: an
  operator MUST be able to widen or narrow one without altering the other. The mechanism
  MUST be [NEEDS CLARIFICATION: two separate versioned policy artifacts the operator
  reviews independently, or one artifact that declares both scopes and a run selects the one
  that applies? Two artifacts make each scope obvious in isolation and doubles what an
  operator must keep in sync; one artifact keeps them adjacent and reviewable together but
  makes "what may a survey do" a question about run mode rather than about a file.]
- **FR-018**: A write outside the grant in force MUST be denied and recorded with a reason,
  and the run MUST continue with its allowed actions.
- **FR-019**: Every run MUST record the identity of the policy that actually governed it, so
  a survey run and an execution run are distinguishable after the fact.
- **FR-020**: A missing or unparseable policy for either scope MUST fail the run before any
  wiki change is made.
- **FR-021**: Widening the write scope MUST NOT widen the read scope, and MUST NOT introduce
  any capability to create or delete pages beyond what is already permitted.

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
- **Survey scope**: the write authority a Lint survey run holds — frontmatter of existing
  pages, excluding the reserved index and activity log.
- **Execution grant**: the write authority an authorized remediation execution run holds,
  derived from what a human authorized.
- **Policy identity**: the version and hash recorded on a run, identifying which scope
  governed it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic harness guarantees**

- **SC-001**: 100% of search results name paths the run's read policy already allows; no
  result surfaces a path a read would have been denied.
- **SC-002**: 100% of denied searches, denied slice reads, denied batch members, and denied
  writes are recorded with a reason and leave the run running.
- **SC-003**: 100% of searches return within their time bound and within their result cap,
  and 100% of truncated or time-bounded results are explicitly marked as incomplete.
- **SC-004**: 100% of body-changing writes attempted during a Lint survey run are denied and
  recorded — the guarantee ADR-016 already makes, unchanged.
- **SC-005**: 100% of body-changing writes attempted by a remediation execution outside the
  grant in force are denied and recorded.
- **SC-006**: 100% of authorized remediation tasks whose proposal targets page content have
  a write scope in force that permits that change — no authorized task is blocked by the
  survey run's frontmatter-only limit.
- **SC-007**: 100% of batches containing a write are rejected without performing any of
  their calls.
- **SC-008**: 100% of write attempts against a page whose current content was not fully read
  in the same run are rejected.
- **SC-009**: 100% of runs record the policy identity that governed them, and a survey run's
  recorded identity differs from an execution run's.
- **SC-010**: 100% of runs whose governing policy is missing or unparseable fail before any
  wiki file changes.

**Agent-judgment evaluation thresholds**

- **SC-011**: On a wiki of at least six hundred pages, ≥ 90% of sampled Lint survey runs
  complete their survey while the total page content they read stays under the run's context
  guard — i.e. the agent narrows with search and slices instead of reading the wiki.
- **SC-012**: ≥ 90% of sampled searches the agent issues are scoped (a pattern narrow enough
  or a path prefix present) rather than an unbounded sweep that immediately truncates.
- **SC-013**: ≥ 90% of sampled authorized body-edit remediations produce a page change that
  a reviewer scores as addressing the authorized proposal.
- **SC-014**: Median content tokens read per Lint survey run on the same fixture drops by
  ≥ 50% against the pre-feature baseline.

## Assumptions

- The existing cross-process write lock and compare-and-swap coordination is reused
  unchanged; this feature adds no second coordination mechanism.
- The reserved index and activity log files keep their current exclusions in every scope.
- Search reads the same content root that reading already reaches, and inherits the run's
  read policy rather than declaring a scope of its own.
- Fail-closed handling of a missing or unparseable policy is retained as-is and extended to
  whatever new scope artifact this feature introduces.
- The three existing tool names keep their current shapes; the slice parameters are additive
  and optional, so instruction files that do not use them behave exactly as today.
- The new retrieval capabilities are offered per agent through the existing per-agent tool
  registries. Lint gets them because it needs them; whether Ingest and Query are offered the
  same capabilities is a registry decision made when each of those agents needs it, and this
  feature does not change their policies.
- The human authorization step itself — who may authorize, and how — is unchanged; this
  feature only changes what an already-authorized execution may write.
- No new infrastructure, no new external system, and no shell or process-execution
  capability for the agent.
- One recorded-eval re-capture covers the instruction-file changes this feature requires,
  per the existing recorded-replay eval approach.
- Sequencing is settled: this feature lands ahead of the "Lint at scale" work (#108), whose
  own spec may assume these capabilities exist.

## Dependencies

- **ADR-006** — the guarded tool-use loop and deny-by-default policy model: this feature adds
  tools and a scope to that boundary and must not create a second one.
- **ADR-011** — the shared agent runtime and tool-registry contract: new tool definitions and
  the registry's unknown-tool rejection govern how a capability is offered per agent.
- **ADR-015** — cross-process write coordination and compare-and-swap: reused unchanged; the
  partial-read rule (FR-010) exists to protect its baseline.
- **ADR-016** — Lint's frontmatter-only write scope: this feature preserves it for survey runs
  and adds a second, distinct scope for authorized execution. An amendment is expected.
- **ADR-018** — remediation authorization and execution: the source of the authorization this
  feature derives a write grant from.
- Replaces issues **#64** (remediation write access) and **#150** (guarded search and batching).
