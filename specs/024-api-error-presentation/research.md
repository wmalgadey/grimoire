# Phase 0 Research: Readable API Error Presentation

**Feature**: 024-api-error-presentation | **Date**: 2026-08-16 | **Spec**: [spec.md](./spec.md)

## Starting position (measured, not assumed)

A survey of the current error path produced the facts this plan is built on:

- **The Hub has no single error shape.** Two ad-hoc conventions coexist across the endpoint files:
  `{ message: "…" }` (human prose — validation and not-found) and `{ reason: "snake_case" }`
  (machine code — 409/503 rejections), sometimes together, sometimes not. `Results.Json(new { … })`
  anonymous objects are built inline at roughly twenty call sites. There is **no** RFC 7807
  `ProblemDetails` anywhere in `backend/src` or `backend/tests`, no shared error-result helper, and
  no exception-handling middleware — an unhandled endpoint exception produces whatever the framework
  defaults to, in a shape unrelated to the deliberate errors beside it.
- **Some rejections ship a machine code and no message at all.** `QuerySubmissionEndpoints.cs:75`
  returns `{ reason = "query_concurrency_limit_reached" }`, `:83` returns
  `{ reason = "conversation_already_active" }`, and `IngestSubmissionEndpoints.cs:555` returns a
  bare `{ reason }` for the restart conflict. For these, there is no human-readable text on the wire
  to display, which is the root of the reported defect.
- **The browser compensates with duplicated tables.** Because the Hub does not always send prose,
  three separate clients carry their own `REASON_MESSAGES` snake_case→prose lookup tables —
  `lintApi.ts:19-26`, `remediationApi.ts:32-41`, `querySubmissionApi.ts:20-23` — each a partial copy
  of the Hub's knowledge of its own failure modes.
- **The compensation is inconsistent with itself.** `httpErrorMessage.ts:14-15` prefers `message`
  then falls back to `reason`; `lintApi.ts` and `remediationApi.ts` prefer `reason` first. The same
  response therefore renders differently depending on which client fetched it. And
  `httpErrorMessage.ts`'s fallback branch displays the raw machine code to the user when no prose
  is present — the literal reported bug.
- **Presentation is one line of small text.** Every error surface is a `$state<string | null>`
  rendered as `<p class="text-sm text-stage-failed">`. There is no banner or toast system, no
  technical-detail affordance, no retry affordance, and no category distinction anywhere.
- **Several failures are swallowed entirely.** `routes/+page.svelte:64-67`, `:78-80`, `:88-90`,
  `routes/tasks/[taskId]/+page.svelte:57-61`, and `remediationLifecycleClient.ts:189` catch and
  discard. These are background refreshes, and silence is a defensible choice for them — but it is
  currently an accidental one, made independently five times.
- **Provider errors already arrive composed and sanitized.** `AnthropicModelClient` translates a
  rejected model request into `Model API error 400 (invalid_request_error): <provider message>`,
  single-lined and capped at 500 characters, and `ErrorSanitizer` redacts credential-shaped strings.
  That text reaches `failureReason` on the board, the task detail, and the status history, where the
  UI prints it verbatim, technical prefix and all.

Two consequences shape every decision below. First, the readable text cannot come from the browser
without permanently duplicating the Hub's failure taxonomy in a second language. Second, the fix has
to be a *contract*, not a patch: twenty inline anonymous objects is exactly the shape that regrows.

---

## R1 — Wire shape for the API error structure

**Decision**: RFC 7807 `application/problem+json`, with the stable machine code carried in an
extension member `code` and the correlation id in an extension member `traceId`. The full member set
this feature relies on is `status`, `title`, `detail`, `code`, `traceId`.

**Rationale**:

- It is the shape ASP.NET Core already produces for framework-generated failures once
  `AddProblemDetails()` and an exception handler are registered. Choosing a bespoke envelope would
  mean the deliberate errors and the framework's own (unhandled exception, 404 on an unmapped route,
  415 from model binding) differ in shape — and FR-004 requires one shape for every failure. Picking
  the standard shrinks the "unexpected response" category to genuine infrastructure noise instead of
  making our own stack a source of it.
- `title` and `detail` map cleanly onto the two-level presentation FR-005/FR-006 require: a short
  headline and the actionable sentence. Inventing our own two-level naming would gain nothing.
- The `application/problem+json` content type makes "is this a recognizable error structure?"
  (FR-013) answerable from the response headers before parsing the body, which is what lets a proxy's
  HTML page be classified without guessing.

**Alternatives considered**:

- *Custom envelope, e.g. `{ error: { code, message, status, traceId } }`.* Rejected: no benefit over
  the standard, and it would leave framework-generated failures in a second shape that the frontend
  must then also handle — reintroducing the branching this feature exists to remove.
- *Keep `{ message, reason }` and merely make both mandatory everywhere.* Rejected: it is the
  cheapest change and it does fix the bare-code display, but it leaves the shape undocumented and
  unenforced, so the twentieth call site regrows the drift. It also keeps two names for one concept
  (`reason` is a code in some responses and prose in none), which is the ambiguity that produced the
  inconsistent client precedence in the first place.
- *Use the `type` URI member for the machine code.* Rejected: RFC 7807 intends `type` to be a
  dereferenceable URI identifying the problem class. We have no documentation URIs to point at, and
  a fake URN would be ceremony. An extension member named `code` says what it is.

**Consequence for existing consumers**: the `{ message }` / `{ reason }` bodies are replaced, not
extended. Frontend and Hub ship from the same repository and no third-party consumer exists
(spec Assumptions), so this is a contained change. Integration tests that assert `reason` or
`message` (`RemediationAuthorizationTests`, `LintConcurrencyAndLivenessTests`,
`QueryConversationRecordFailClosedTests`, `IngestTaskRecordApiTests`, `BoardCompositeResponseTests`,
`IngestSubmissionPromptApiTests`) assert a product-owned contract and must be migrated to the new
member names — they are kept and sharpened, not deleted (Principle II, "straddling tests are
rewritten, not deleted").

---

## R2 — Where the human-readable message is authored

**Decision**: a single catalogue in the Hub maps each stable code to its `status`, `title`, and a
default `detail`. Endpoints reference a catalogue entry rather than composing an error body inline.
A call site may supply a more specific `detail` (to name a task id, for example); if it does not,
the catalogue's default is used. An unknown code resolves to a generic entry.

**Rationale**:

- FR-016 requires every failure the API can return to have a defined readable message, and SC-006
  asserts that at 100%. With messages as literals at twenty call sites, that guarantee can only be
  established by an audit — a human reading every endpoint. With a catalogue it is a test over a
  real collection: every entry has a non-empty title and detail, and every code the endpoints
  actually emit exists in the catalogue.
- It puts operator-facing message text on the side of the boundary that owns it. The Hub knows why
  it declined; the browser does not, and today pretends to via three partial copies.
- It makes the codes enumerable, which is what the frontend needs in order to *stop* enumerating
  them.

**Alternatives considered**:

- *Message literals at each call site, no catalogue.* Rejected for the SC-006 reason above: the
  guarantee becomes unverifiable, which under Principle IV means it does not exist.
- *Resource/localization files.* Rejected as premature — i18n is issue #39 and explicitly out of
  scope. A catalogue keyed by code is the structure i18n would later need anyway, so this does not
  paint that corner.

**Note on Principle V**: error copy is operator-facing harness text, not wiki content. Authoring it
in backend code is correct and is not the "wiki-content judgment in deterministic code" the
principle forbids.

---

## R3 — Correlation identifier

**Decision**: the Hub populates `traceId` from the ambient trace context (`Activity.Current`),
falling back to the request identifier when no activity is in scope, and does so inside our own
error-result helper rather than relying on a framework customization hook.

**Rationale**: the helper's output is then a product-owned contract that a test can assert directly
(Principle II, "Test what we own" — the ownership test asks whether the assertion could fail from a
change to Grimoire's own source alone, and here it could). Relying on a framework hook to enrich the
body would make the test a check that the framework calls its own callback, which is precisely the
library-owned behaviour the constitution excludes.

**Alternatives considered**: emitting the correlation id as a response header instead of a body
member. Rejected — the frontend must display it in the technical detail (FR-006), and a header is
strictly harder to reach in the branch where the body already has to be read.

---

## R4 — Failure classification

**Decision**: four categories, determined by **what happened to the request**, not by whether the
body parsed:

| Category | Condition | Retry offered |
|---|---|---|
| `unreachable` | no HTTP response at all (connection failure, DNS, timeout, offline) | Yes |
| `declined` | the Hub answered 4xx | No |
| `fault` | the Hub or something in front of it answered 5xx | Yes |
| `unexpected` | a response arrived that the client cannot use as either a success or a recognizable declined-request error | No |

Body recognizability is orthogonal: it decides *how much can be said* about the failure (whether a
`title`/`detail` from the Hub is available or a generic message must be used) and is surfaced in the
technical detail, never in the category.

**Rationale**: this is the correction recorded in the requirements checklist, item 4. Classifying by
body parseability makes retryability incoherent for the most common real infrastructure failure — a
gateway answering 502 with an HTML page is maximally worth retrying, and body-driven classification
would file it as "unexpected" and withhold the retry. What the user needs to decide is *check my
connection / fix my input / wait and retry*, and that decision follows from what happened to the
request.

**Alternatives considered**: collapsing `unexpected` into `fault`. Rejected — it is genuinely
useful for the operator to distinguish "the Hub told us it failed" from "something answered in a
shape we do not understand", and the two have different first diagnostic steps (read the Hub log
vs. look at what sits between browser and Hub).

---

## R5 — Frontend presentation

**Decision**: one module converting a failed request into a presented error, and one component
rendering it. Both replace `httpErrorMessage.ts` and the three `REASON_MESSAGES` tables, which are
deleted rather than left beside the new path.

- The module exposes the category, the primary message, the retryability, and the technical detail
  (status, code, traceId, bounded body excerpt where the body was unrecognizable).
- The component renders the primary message in a distinct region marked as an alert for assistive
  technology, with the technical detail behind a disclosure and a retry control when retryable.

**Rationale**: FR-010 and SC-005 require one presentation across every surface, and the existing
per-surface `$state<string | null>` + `<p>` pattern is duplicated across at least eleven components
and routes. Leaving the old helper in place beside the new one is how the duplication regrows;
deletion is what makes SC-005 assertable.

**Retry semantics**: the component raises a retry request; each surface supplies the action to
re-run. The component does not itself know how to retry, which keeps it free of surface-specific
knowledge.

**Background-refresh failures stay silent.** The five currently-swallowed catch sites are background
refreshes, not user actions, and the spec's edge case requires that a failing background refresh not
displace an error the user is reading about their own action. They keep their current behaviour;
what changes is that the silence becomes a stated decision with a comment, rather than five
independent accidents.

---

## R6 — Scope boundary against recorded failure data

**Decision**: recorded agent-run failure text (`failureReason`, `outcomeReason`, status-history
`detail`) is rendered through the new presentation, but its recorded form is unchanged.

**Rationale**: the recorded text is already single-lined, capped, and sanitized on the way in, and
three writers plus the status-history table depend on its current shape. Restructuring it into
code/message/status parts would touch the artifact format, the run-event protocol, and the
status-history schema — a materially larger feature, and the one issue #88 will need. Presenting it
better costs none of that: the technical prefix (`Model API error 400 (invalid_request_error): `) is
demoted to the technical detail and the provider's own sentence becomes the primary message.

**Alternatives considered**: parsing the recorded string in the frontend to split prefix from
message. Accepted only in the narrow, documented form above (a known prefix produced by our own code
is stripped for display); rejected as a general strategy, because string-matching arbitrary recorded
text to infer structure is the fragility this feature is meant to remove.

---

## Open items carried into the plan

None. All three decisions that `/speckit-clarify` would have raised are recorded in the spec's
Clarifications section; the one internal contradiction found during this research is corrected in
the spec and logged in the requirements checklist.
