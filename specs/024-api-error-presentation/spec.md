# Feature Specification: Readable API Error Presentation

**Feature Branch**: `claude/next-feature-issue-afw3vx` (spec directory `024-api-error-presentation`; the
harness pinned the working branch for this session, so branch and directory names differ)

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: "setzt #85 vollständig um, nutze den vollen spec-kit workflow ohne rückfragen"

Source: GitHub issue [#85](https://github.com/wmalgadey/grimoire/issues/85) — "Bug: Surface API
error details clearly instead of raw HTTP/JSON output". The issue's own framing:

> API failures are currently shown in a developer-centric format: the HTTP status code plus the raw
> JSON response body. This is technically accurate, but not easy for end users to understand or act on.

Its stated acceptance direction, carried into this spec:

> - Users see a clear error message first, not raw JSON.
> - Known API error payloads are rendered into readable text.
> - Debug details remain accessible without cluttering the main UI.
> - The UI distinguishes between network failures, HTTP failures, and API-declared validation or quota errors.

## Clarifications

### Session 2026-08-16

The operator asked for the full workflow **without questions** (`ohne rückfragen`). The three
decisions that would otherwise have been asked in `/speckit-clarify` were therefore made here and
are recorded as decisions, not as open questions:

- Q: Should the readable message be produced in the browser (mapping known machine codes to text)
  or served by the Hub as part of its error response? → **A: Served by the Hub.** Operator-facing
  message text is a product-owned contract (Constitution Principle II, "Test what we own"); placing
  it in the browser would duplicate the Hub's knowledge of its own failure modes across two
  code bases and make the text untestable from the side that decides it.
- Q: Should agent-run failure reasons (e.g. a rejected model request) also be restructured by this
  feature? → **A: No — presentation only.** What the harness *records* for a failed run is
  unchanged; this feature only guarantees that the recorded reason is *displayed* through the same
  presentation, with the technical portion demoted to on-demand detail. Restructuring recorded
  failure data is issue #88's territory.
- Q: One active error per surface, or an accumulating list? → **A: One active error per surface**,
  replaced by a newer one and cleared by a subsequent success. An error log is operational
  telemetry, not UI furniture.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Understand a rejected request without reading JSON (Priority: P1)

As someone using the web UI, when the system refuses what I asked for, I want to be told in plain
language what went wrong and what I can do about it, so that I can act on the failure instead of
decoding a status code or an internal identifier.

**Why this priority**: This is the reported defect. Today a refusal surfaces either as
`Request failed with status 409` — which says nothing — or as an internal identifier such as
`conversation_already_active` or `query_concurrency_limit_reached`, which is a name meant for logs
and dashboards, not for a person deciding what to do next. Every other story in this spec is an
improvement on top of a message that is already readable; without this one there is nothing to
improve.

**Independent Test**: Trigger any refusable action (submit a question while that conversation is
already busy; start a lint run while remediation tasks are unresolved; restart a task that is not in
a failed state). Confirm the visible primary message is a sentence a non-developer can act on, and
that no status code, internal identifier, JSON punctuation, or field name appears in it.

**Acceptance Scenarios**:

1. **Given** an action the system declines for a stated reason, **When** the user triggers it,
   **Then** the surface shows a human-readable sentence describing what happened and what the user
   can do, and the raw response body is not shown as the primary message.
2. **Given** a refusal whose internal identifier is `conversation_already_active`, **When** the
   error is displayed, **Then** the identifier does not appear in the primary message, and the
   primary message instead states in plain language that the conversation is still working on the
   previous question.
3. **Given** an action that succeeds after a previous failure on the same surface, **When** it
   completes, **Then** the previously shown error is no longer displayed.
4. **Given** two failures in a row on the same surface, **When** the second occurs, **Then** the
   surface shows the second failure only — errors replace rather than accumulate.

---

### User Story 2 - Keep the technical detail, one step away (Priority: P1)

As someone diagnosing a failure — the operator of this wiki, who is also its developer — I want the
technical facts about the failure to remain available on demand, so that making the message readable
for a person does not cost me the information I need to file a bug or read a log.

**Why this priority**: Equal in priority to User Story 1 and inseparable from it. A "clean message"
that discards the status code, the internal identifier, and the correlation id would trade one
unusable state for another: the operator would then have to reproduce the failure with developer
tools open to learn anything. The issue is explicit that debug detail must survive.

**Independent Test**: Trigger any failure, confirm the primary message carries no technical noise,
then open the failure's detail affordance and confirm the status code, the stable internal
identifier, and the correlation identifier for the failed request are all present.

**Acceptance Scenarios**:

1. **Given** a displayed error, **When** the user opens its technical detail, **Then** the response
   status, the stable internal identifier for the failure, and the correlation identifier of the
   request are shown.
2. **Given** a displayed error, **When** the user has not opened the technical detail, **Then**
   none of those technical facts occupy the primary message area.
3. **Given** a failure whose response carried no recognizable structure, **When** the user opens the
   technical detail, **Then** a bounded excerpt of what was actually received is shown, so the
   operator can tell an unreachable proxy from a genuine server fault.
4. **Given** an error whose underlying description is longer than the primary message area allows,
   **When** it is displayed, **Then** the primary message is shortened for reading and the full text
   remains available in the technical detail without loss.

---

### User Story 3 - Notice that something failed (Priority: P2)

As someone using the web UI, I want a failure to be visually prominent and announced, so that I do
not sit waiting for a result that will never arrive because I missed a line of small text.

**Why this priority**: Independent of message quality — a perfectly worded message that renders as
one line of small coloured text below a form is still missed. Valuable on its own, but it improves
the *delivery* of the message rather than the message, so it ranks below the two P1 stories.

**Independent Test**: Trigger a failure on each surface that can produce one and confirm the error
is rendered in a consistent, visually distinct region that assistive technology announces, rather
than as incidental body text.

**Acceptance Scenarios**:

1. **Given** any surface that can fail, **When** a failure occurs, **Then** the error appears in a
   consistently styled, visually distinct region rather than as ordinary body text.
2. **Given** an error appears while the user is focused elsewhere on the page, **When** it is
   rendered, **Then** it is announced to assistive technology without stealing keyboard focus.
3. **Given** a displayed error, **When** the user dismisses it, **Then** it is removed from the
   surface and does not reappear until a new failure occurs.

---

### User Story 4 - Tell apart "you're offline", "it broke", and "it said no" (Priority: P2)

As someone using the web UI, I want to be able to tell whether the system never heard my request,
heard it and refused it, or heard it and broke, so that I know whether to check my connection, fix
my input, or wait and retry.

**Why this priority**: These three failures demand three different responses from the user, and
today they are indistinguishable — all three arrive as some variation of "request failed". Ranked
below the P1 stories because a readable message is already a large improvement even when its
category is implicit, but the categories are what make the message *actionable*.

**Independent Test**: Produce each of the four categories — unreachable host, declined request,
server fault, unrecognizable response — against the same surface and confirm each is presented
distinguishably, with retry offered only where retrying could plausibly succeed.

**Acceptance Scenarios**:

1. **Given** the Hub cannot be reached at all, **When** the user submits, **Then** the error is
   presented as a connectivity problem, names the system as unreachable rather than as refusing,
   and offers a retry.
2. **Given** the Hub declines the request for a stated reason, **When** the error is displayed,
   **Then** it is presented as a declined request carrying that reason.
3. **Given** the Hub fails internally, **When** the error is displayed, **Then** it is presented as
   a system fault, stated as not the user's fault, and offers a retry.
4. **Given** an HTTP failure the system declined but whose body is not a recognizable error
   structure, **When** the error is displayed, **Then** it is presented as an unexpected response
   with a generic readable message, and no raw body content is shown as the primary message.
5. **Given** a system fault whose body is not a recognizable error structure either, **When** the
   error is displayed, **Then** it is still presented as a system fault and still offers a retry —
   an unreadable body changes what can be *said* about the failure, not whether retrying it can
   succeed.

---

### User Story 5 - No surface left behind (Priority: P3)

As the operator, I want every place in the UI that can show a failure to use the same presentation,
so that the fix does not decay the next time a surface is added and one more corner starts printing
raw responses again.

**Why this priority**: A durability concern rather than a new capability — the four stories above
are already valuable if applied one surface at a time. It ranks last because it delivers no new
behaviour, only the guarantee that the behaviour is uniform.

**Independent Test**: Enumerate every surface that displays a request failure (ingest submission,
question submission, lint trigger, remediation action, task restart, task and board loading) and
confirm each renders through the same presentation with the same categories and the same detail
affordance.

**Acceptance Scenarios**:

1. **Given** any surface in the UI that can display a request failure, **When** it fails, **Then**
   its error is rendered through the shared presentation rather than a surface-specific one.
2. **Given** a recorded failure reason for an agent run that already failed, **When** it is shown in
   the task views, **Then** it is rendered through the same presentation, with any technical prefix
   demoted to the detail affordance.

---

### Edge Cases

- **Body is not the expected structure**: a gateway or proxy answers with HTML, or the body is
  empty. The category still follows what happened to the request — a faulting gateway is a system
  fault and stays retryable (User Story 4, scenario 5); only a declined request with an
  unrecognizable body becomes an unexpected response (scenario 4). In both cases the received
  content appears solely as a bounded excerpt inside the technical detail, never as the primary
  message.
- **Very long underlying text**: a rejected model request can carry a description hundreds of
  characters long. The primary message is shortened; the full text stays in the technical detail
  (User Story 2, scenario 4).
- **Secrets echoed in failure text**: the Hub already redacts credential-shaped strings from
  recorded failure text. This feature must not introduce a path that displays unredacted text — in
  particular the bounded excerpt of an unrecognized body must come from the response the Hub
  produced and must be length-capped.
- **Failure with no correlation identifier**: an error raised before a request was correlated has no
  identifier to show. The technical detail omits the field rather than displaying an empty or
  placeholder value.
- **Failure during an in-flight background refresh**: a periodic refresh failing must not replace an
  error the user is currently reading about their own explicit action.
- **Request declined for a reason the presentation does not recognize**: a new failure identifier
  shipped by the Hub without a matching readable message must still produce a readable, generic
  message rather than falling back to printing the identifier.
- **Simultaneous connection-status loss**: the live connection indicator is a separate, existing
  surface. A dropped live connection must not also raise a request error, and vice versa.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST answer every declined or failed API request with a single, consistent
  error structure that carries, at minimum: a human-readable message, a stable machine-readable
  identifier for the failure, the response status, and the correlation identifier of the request.
- **FR-002**: The human-readable message in that structure MUST be written for a person acting on
  the failure — a sentence stating what happened and, where a user action can resolve it, what to
  do. It MUST NOT be an internal identifier, a field name, or a serialized structure.
- **FR-003**: The stable machine-readable identifier MUST remain available and unchanged in meaning
  for consumers that key on it (logs, tests, telemetry); making the message readable MUST NOT be
  achieved by removing the identifier.
- **FR-004**: Every API endpoint that can decline or fail a request MUST use the structure from
  FR-001; no endpoint may answer a failure with an ad-hoc or differently-shaped body.
- **FR-005**: The UI MUST render the human-readable message as the primary content of any error it
  displays, and MUST NOT render the response body, the status code, or the machine-readable
  identifier as that primary content.
- **FR-006**: The UI MUST provide, for every displayed error, an affordance that reveals the
  technical detail — status, machine-readable identifier, correlation identifier, and where the
  response was unrecognizable, a bounded excerpt of what was received — without that detail
  occupying the primary message area when unopened.
- **FR-007**: The UI MUST classify every request failure into exactly one of four categories —
  system unreachable, request declined, system fault, unexpected response. Classification MUST be
  driven by *what happened to the request* (no response at all; the system declined it; the system
  faulted), not by whether the response body could be parsed. Whether the body was recognizable
  determines only how much can be *said* about the failure, and is reflected in the technical
  detail. The category MUST be presented distinguishably.
- **FR-008**: The UI MUST offer a retry affordance for the categories where retrying can plausibly
  succeed (system unreachable, system fault) and MUST NOT offer one where the request was declined
  for a reason the user must first resolve.
- **FR-009**: The UI MUST render errors in a consistently styled, visually distinct region that is
  announced to assistive technology without moving keyboard focus.
- **FR-010**: Every UI surface capable of displaying a request failure MUST render it through one
  shared presentation; surface-specific error formatting MUST NOT remain.
- **FR-011**: A displayed error MUST be replaced by a newer failure on the same surface, MUST be
  cleared by a subsequent success on that surface, and MUST be dismissible by the user.
- **FR-012**: A recorded agent-run failure reason displayed in the task views MUST be rendered
  through the same presentation, with any technical prefix demoted to the technical detail. What the
  harness records for a failed run MUST NOT change.
- **FR-013**: A failure whose response carries no recognizable error structure MUST still produce a
  readable primary message; the system MUST NOT fall back to displaying the raw body or the status
  code as that message.
- **FR-014**: A message displayed in the primary area MUST be length-bounded for readability, and
  any text elided from it MUST remain reachable in full through the technical detail.
- **FR-015**: The system MUST NOT display credential-shaped or otherwise redacted content; the
  existing redaction of recorded failure text remains authoritative and MUST cover every path this
  feature adds.
- **FR-016**: Every distinct failure the API can return MUST have a defined human-readable message;
  an identifier without one MUST still yield a readable generic message rather than the identifier
  itself.

### Key Entities

- **API Error Response**: what the system returns when it declines or fails a request. Carries a
  human-readable message, a stable machine-readable failure identifier, the response status, and the
  request's correlation identifier. One shape for the whole API.
- **Presented Error**: what the user sees. Carries a category (unreachable / declined / fault /
  unexpected), a primary readable message, an optional retry affordance, and a collapsed technical
  detail derived from the API Error Response or, where none was received, from what was observed.

## Success Criteria *(mandatory)*

This is a harness-only feature: nothing in it is a judgment an LLM agent makes about wiki content
(Constitution Principle V). Accordingly **all** success criteria below are deterministic guarantees,
and there are deliberately **no agent-judgment thresholds** — attaching one here would be a spec
defect in the opposite direction.

### Measurable Outcomes

- **SC-001**: 100% of API responses that decline or fail a request carry the error structure from
  FR-001 with all four of its fields populated (or the correlation identifier explicitly absent
  where none exists).
- **SC-002**: 100% of failures displayed in the UI show a human-readable message as their primary
  content — zero display a status code, a machine-readable identifier, or serialized body content
  in that position.
- **SC-003**: 100% of displayed failures expose the technical detail — status, machine-readable
  identifier, and correlation identifier where present — through the detail affordance.
- **SC-004**: 100% of the four failure categories are presented distinguishably, and the retry
  affordance appears for exactly the two retryable categories.
- **SC-005**: 100% of UI surfaces that can display a request failure render it through the shared
  presentation; zero surfaces retain their own error formatting.
- **SC-006**: 100% of the API's distinct failure identifiers resolve to a defined human-readable
  message, and an unknown identifier still yields a readable generic message in 100% of cases.
- **SC-007**: 100% of displayed error text is length-bounded in the primary area, with the full text
  recoverable from the technical detail.
- **SC-008**: 100% of failure text paths preserve the existing credential redaction — zero display
  paths reveal content the recording path had redacted.

## Assumptions

- The web UI and the API ship together from this repository; no third-party consumer depends on the
  current, inconsistent error body shapes, so replacing them with one structure is a contained
  change rather than a breaking public-API change.
- The correlation identifier needed by FR-001 is the one the system already attaches to a request
  for tracing; this feature surfaces it rather than introducing a new one.
- Redaction of credential-shaped strings in failure text already exists on the recording path and is
  the authority for FR-015; this feature adds no new redaction rules, only the obligation not to
  bypass the existing one.
- UI copy is English-only. Translating it is issue #39 (i18n) and is out of scope here.
- The live-connection indicator is an existing, separate surface with its own presentation; request
  errors and connection state remain distinct concerns.

## Out of Scope

- Changing what the harness *records* for a failed agent run (failure reason text, artifact fields,
  status-history entries). This feature changes presentation only.
- Handling oversized model inputs, chunking, or any strategy for prompt-length failures — that is
  issue #88.
- Retry policy, backoff, or automatic recovery behaviour beyond offering a manual retry affordance.
- Translating or localizing error copy (issue #39).
- The live-connection status indicator and its reconnection behaviour.
- Error presentation in the command-line surface; this feature covers the API and the web UI.
