---
status: accepted
---

# ADR-026: Hub API Error Response Contract and Shared Frontend Error Presentation

> **Extends [ADR-020](ADR-020-hub-cli-command-surface.md)**: ADR-020 fixed the *CLI's*
> failure contract — exit codes plus a stdout/stderr split — and left the HTTP surface's
> failure shape undecided. This ADR supplies the HTTP counterpart and supersedes no part
> of ADR-020: its exit codes, stdout result contract, telemetry-flush behaviour,
> containment rule C9, and its CLI↔HTTP parity clause are unchanged, and this decision is
> deliberately placed so that parity clause keeps holding.
>
> **Extends [ADR-013](ADR-013-unified-agent-platform-packaging-and-naming.md)**: registers
> one new namespace, `Grimoire.Hub.ApiErrors`, in rule N1's Hub namespace-ownership map as
> Cross-agent, following the precedent ADR-020 set for `Grimoire.Hub.Cli`. N1 itself, the
> agent-token naming rule, the exemption-fixture mirror requirement, and the frozen
> per-agent OTel identities are unchanged.

## Context and Problem Statement

The Hub answers a failed HTTP request in one of two ad-hoc shapes, chosen per call site:
`{ message: "…" }` (human prose) or `{ reason: "snake_case" }` (a machine code). Some
responses carry only the code and no prose at all —
`QuerySubmissionEndpoints` returns bare `{ reason = "query_concurrency_limit_reached" }`
and `{ reason = "conversation_already_active" }`, and `IngestSubmissionEndpoints` returns
a bare `{ reason }` for the restart conflict. There is no RFC 7807 `ProblemDetails`
anywhere in `backend/src`, no shared error-result helper, and no exception-handling
middleware of any kind: an unhandled exception escaping a minimal-API handler falls
through to Kestrel as a bare 500 with an empty body, in a shape unrelated to the
deliberate errors beside it.

The browser compensates. Three separate clients — `lintApi.ts`, `remediationApi.ts`,
`querySubmissionApi.ts` — each carry their own `REASON_MESSAGES` table mapping snake_case
codes to prose, each a partial copy of the Hub's knowledge of its own failure modes. Two
parse helpers disagree about precedence (`httpErrorMessage.ts` prefers `message` then
falls back to `reason`; the other two prefer `reason`), so the same response renders
differently depending on which client fetched it, and one path displays the raw machine
code to the user. Every surface then renders the result as a single line of small text —
no category, no technical detail, no retry.

Feature 024 (`specs/024-api-error-presentation/spec.md`, from issue #85) requires one
consistent error structure carrying a human message, a stable machine code, the status,
and a correlation identifier; and one shared frontend presentation with four failure
categories, a technical-detail disclosure, and a retry affordance on the retryable ones.

Three things about that are architectural rather than task-level, which is why this ADR
exists rather than the decisions being improvised in `tasks.md`:

1. **A Hub-wide HTTP failure contract has never been decided.** It spans five
   independently-owned endpoint namespaces (`IngestSubmission`, `QuerySubmission`,
   `QueryConversations`, `LintDispatch`, `RemediationTasks`), each governed by its own ADR
   (014, 018, 025) that fixed *outcomes* but never a *shape*. The absence of that decision
   is precisely why two shapes drifted into existence.
2. **Where the envelope is composed is load-bearing for ADR-020.** CLI commands and HTTP
   handlers call the same coordinator and transition-service methods. Composing an HTTP
   response shape inside those shared methods would push it into the CLI path, whose
   failure contract is exit codes and a stdout/stderr split.
3. **Projecting a correlation identifier into a client-visible response body is a new
   integration pattern.** Today the correlation identity exists purely as telemetry
   (Constitution Principle IV; ADR-013 freezes `task_id`/`turn_id` as span attributes).
   Nothing authorizes exposing a trace identity to a browser, and nothing says how to
   obtain it without falling into the production-wiring trap Principle IV was written
   about.

A fourth, less obvious gap: **the frontend has no architectural governance at all.** After
ADR-001 fixes SvelteKit, no ADR governs frontend structure or conventions, and every
enforcement mechanism the repository owns (`Grimoire.ArchTests`, NetArchTest, Mono.Cecil IL
scans) is .NET-only — nothing here can structurally scan TypeScript or Svelte. Yet the
feature's SC-005 ("zero surfaces retain their own error formatting") is a containment claim,
and Constitution Principle IV is absolute that a rule without a CI gate does not exist. How
that claim is enforced therefore has to be decided, not assumed.

## Decision Drivers

- FR-001/FR-004: one structure for every failing response, including the unhandled path.
- FR-002/FR-016 + SC-006: every failure the API can return has authored, readable prose,
  and that completeness is verifiable rather than audited by eye.
- FR-003: existing machine codes stay stable in meaning — logs, tests, and operational
  tooling key on them. `conversation_record_unreadable` is ADR-pinned (ADR-014) and
  `lint_run_active` is named in ADR-020.
- ADR-018's rule that "the loser is rejected and surfaced to the caller (the board shows
  the actual outcome)" — conflicts may not collapse into one generic message.
- ADR-020's CLI↔HTTP parity clause and its separate failure contract.
- Constitution Principle IV's production-wiring rule for observability contract tests, and
  its "conventions not enforced by CI/CD do not exist".
- Constitution Principle II's "Test what we own": the decision must not create a surface
  whose only meaningful tests would assert framework behaviour.
- ADR-001's recorded mitigation that the UI is built from a small set of central reusable
  components rather than off-the-shelf widgets.
- Minimal surface: no new assembly, no new port, no new tooling.

## Considered Options

### Wire shape
1. **RFC 7807 `application/problem+json`, with the machine code in an extension member
   `code` and the correlation id in an extension member `traceId`.**
2. A bespoke envelope, e.g. `{ error: { code, message, status, traceId } }`.
3. Keep `{ message, reason }` but make both members mandatory everywhere.

### Where the readable prose is authored
A. **A catalogue in the Hub mapping each code to status, title, and default detail**;
   endpoints reference an entry, optionally overriding `detail`.
B. Prose as literals at each call site.
C. Resource/localization files.

### Where the envelope is composed
i. **At the HTTP endpoint boundary, in a dedicated namespace, above the shared coordinator
   layer.**
ii. Inside coordinators and transition services, where the outcome is decided.

### How the correlation identifier is obtained
α. **Read from `Activity.Current` inside our own error-result helper**, falling back to the
   request identifier.
β. Via ASP.NET Core's `AddProblemDetails(options.CustomizeProblemDetails)` enrichment hook.
γ. As a response header rather than a body member.

### How SC-005 (frontend containment) is enforced
I. **Feature-Scoped Invariant: classicist Vitest behavioural tests per surface, plus
   deleting the helpers there is nothing to fall back to.**
II. A new frontend lint rule (ESLint) as an additional CI gate.
III. A CI smoke check in the ADR-019 style.

## Decision Outcome

Chosen: **Option 1** (wire shape), **Option A** (catalogue), **Option i** (placement),
**Option α** (correlation id), **Option I** (frontend enforcement).

### The envelope

Every Hub HTTP response that declines or fails a request is `application/problem+json` with
members `status`, `title`, `detail`, `code`, and optional `traceId`. RFC 7807's `type` and
`instance` are unused; adding them later is additive. The full contract, including the
carried-over code table, is `specs/024-api-error-presentation/contracts/api-error-envelope.md`.

RFC 7807 is chosen over a bespoke envelope because it is the shape ASP.NET Core already
produces for framework-generated failures once an exception handler is registered — so the
deliberate errors and the framework's own arrive identically, which is what FR-004 demands
and what a bespoke shape would structurally prevent. The media type also makes "is this a
recognizable error structure?" answerable from the response headers, which is what lets a
proxy's HTML page be classified without guessing. Option 3 was rejected because it is the
cheapest fix and still leaves the shape undocumented and unenforced, so the next call site
regrows the drift; it also keeps two names for one concept, which is what produced the
inconsistent client precedence in the first place.

### The catalogue and its namespace

A catalogue in the new namespace `Grimoire.Hub.ApiErrors` maps each stable `code` to its
`status`, `title`, and default `detail`. A call site may override `detail` with a more
specific sentence; it may not override `code`, `title`, or `status`. An unknown code
resolves to a generic entry rather than echoing the identifier.

The catalogue, not literals at call sites, is what makes SC-006 a test over a real
collection instead of a human audit — and under Principle IV an unverifiable guarantee is
not a guarantee. Localization files (Option C) were rejected as premature: i18n is issue
#39 and out of scope, and a catalogue keyed by code is the structure i18n would need
anyway, so this does not paint that corner.

Per ADR-018 and ADR-025, each distinct decline outcome gets its **own** entry — the two
restart declines (task not `failed`; normalized source missing) and each remediation
transition conflict are separate entries, not one shared "request declined".

`Grimoire.Hub.ApiErrors` is registered as **Cross-agent** in ADR-013's Hub
namespace-ownership map in `docs/conventions/agent-artifact-naming.md`, mirrored into
`AgentArtifactNamingRuleTests` in the same change. It serves the ingest, query, lint, and
remediation endpoint families and the Hub itself, so unprefixed type and test-class names
are correct there (precedent: `HubRequestTracingTests`).

### Placement: the HTTP endpoint boundary

The envelope is composed at the HTTP endpoint boundary and **never inside a coordinator,
transition service, or any other type the CLI also calls**. The exception handler is
registered in `HubHostComposition`, alongside `AddHubTelemetry` — not in `Program.cs`,
which ADR-023 reduced to a one-line pass-through.

This is the decision that keeps ADR-020 intact. Composing inside coordinators is the
natural-looking choice, because that is where the outcome is decided; it is also how the
CLI would silently acquire an HTTP response shape it has no use for, in violation of its
own exit-code/stdout contract and of the parity clause that makes both surfaces share one
implementation.

### Correlation identifier

`traceId` is read from `Activity.Current` inside our own error-result helper, falling back
to the request identifier, and omitted from the payload entirely when neither exists —
never serialized empty or as a placeholder.

Reading it in our own helper rather than through the framework's enrichment hook (Option β)
is a Principle II decision: the helper's output is then a product-owned contract that a test
can falsify by changing Grimoire's own source, whereas asserting the hook fires would be a
test that the framework calls its own callback. A response header (Option γ) was rejected
because the frontend must render the value in the technical detail, and a header is strictly
harder to reach in the branch where the body is already being read.

Exposing a trace identity to a browser is judged safe here: it is an opaque correlation
value carrying no user, credential, or content data, and it is the only thing that lets an
operator join a screenshot to a log line. Its production-wiring verification is covered by
the structural rules below.

### Redaction

The Hub acquires **no** redactor, and must not. `Grimoire.Hub.csproj` references only
`Grimoire.Domain`, and ADR-002's `HubAgentDispatchBoundaryRuleTests` forbids a Hub→agent
reference — so reusing `ErrorSanitizer` (which lives in `Grimoire.AgentRuntime.Composition`)
would mean inverting a dependency direction the Hub has never held, to solve a problem it
does not have. Three facts close FR-015 without it: catalogue text is authored and contains
no runtime data; an unhandled exception's message reaches the log and never the response
body; and recorded agent failure text was already sanitized inside the agent process before
it reached the Hub.

### Frontend enforcement

SC-005 is a **Feature-Scoped Invariant**, covered by classicist Vitest behavioural tests —
one per surface, asserting that the surface renders the shared presentation's observable
structure — and backed structurally by deleting `httpErrorMessage.ts` and the three
`REASON_MESSAGES` tables, so there is no fallback path to regress to. The existing
`bun run test` step in the frontend CI job is the gate; Principle IV is satisfied without a
new one.

A new ESLint rule (Option II) was rejected as new tooling for a rule whose real concern —
surfaces silently reverting to bespoke formatting — is better caught by a test that exercises
the surface than by a lint pattern that a differently-shaped regression would slip past.
Per Constitution Principle III, the Feature-Scoped Invariant test must assert that concern
directly and must **not** assert a bare cardinality ("exactly N error components") as an end
in itself.

### Structural rules

Classified per Constitution v1.11.0.

- **BR1 — Boundary Rule.** The error-producing result factories on
  `Microsoft.AspNetCore.Http.Results` and `TypedResults` — `BadRequest`, `Conflict`,
  `NotFound`, `UnprocessableEntity`, `Problem`, `ValidationProblem`, `Json`, `StatusCode` —
  MUST NOT be called from any `Grimoire.Hub` namespace other than `Grimoire.Hub.ApiErrors`.
  Enforced by a `Grimoire.ArchTests` Mono.Cecil IL scan (the `RuntimePathsBoundaryRuleTests`
  idiom), written in Phase 0 with a Red/Green probe before feature code. No allow-list is
  needed: a survey confirms all five current `Results.Json` call sites are error paths and
  `TypedResults` is unused anywhere in the Hub. This is a durable dependency-direction rule —
  it holds however any endpoint family's surface grows, and every future endpoint inherits it.

- **FSI1 — Feature-Scoped Invariant.** Every catalogue entry has a non-empty title and
  detail; no detail contains its own code; every code an endpoint emits exists in the
  catalogue. Covered by a classicist integration test over the real catalogue and real HTTP
  responses — never by reflection over type shape.

- **FSI2 — Feature-Scoped Invariant.** Every UI surface that can display a request failure
  renders through the shared presentation. Covered as described under "Frontend enforcement".

Both invariants are expected to change when this feature's own surface changes (a new
endpoint family, a new surface), which is exactly why neither receives a reflection/IL-based
test.

### Scope boundaries fixed by this ADR

- **Agent-facing denial reasons are out of scope.** The ten strings fixed by ADR-015,
  ADR-016 and ADR-017 (`create_only_target_exists`, `write_conflict_stale_read`,
  `write_coordination_timeout`, `frontmatter_only_target_missing`,
  `frontmatter_only_malformed_document`, `frontmatter_only_body_changed`,
  `log_entry_not_appended`, `log_entry_malformed_heading`, `log_entry_missing_paragraph`,
  `catalog_entry_malformed`) are addressed to the model, delivered as `is_error` tool
  results, and reach a human only as `deniedActions` on a task artifact. FR-016's "every
  distinct failure the API can return" means the HTTP API surface only.
- **The CLI keeps its own contract.** ADR-020's exit codes and stdout/stderr split are
  unchanged and this envelope does not apply to them.
- **The SignalR lifecycle channel keeps its own presentation.** ADR-011 made it a
  structurally separate surface; `ConnectionStatusIndicator` is not absorbed.
- **Recorded failure data is unchanged.** What the harness records for a failed agent run
  (task-artifact `failure_reason`, run events, status-history `detail`) is untouched;
  ADR-002's agent-owned artifact and ADR-008's event channel stand.

### Consequences

- Good, because the failure vocabulary becomes a governed contract with a completeness
  guarantee, in one place, instead of strings scattered across five endpoint namespaces and
  three browser lookup tables.
- Good, because the unhandled-exception path stops being a shape of its own — a bare 500
  with an empty body — and becomes indistinguishable in form from a deliberate rejection,
  which is what lets the client have one branch instead of two.
- Good, because BR1 makes the regrowth mechanism structurally impossible rather than a
  review responsibility: the twenty-first inline anonymous error body fails the build.
- Good, because the placement decision keeps ADR-020's parity clause honest instead of
  quietly eroding it.
- Bad, because every current integration test asserting `reason` or `message` must be
  migrated to the new member names — `RemediationAuthorizationTests`,
  `LintConcurrencyAndLivenessTests`, `QueryConversationRecordFailClosedTests`,
  `IngestTaskRecordApiTests`, `BoardCompositeResponseTests`,
  `IngestSubmissionPromptApiTests`. These assert a product-owned contract, so per Principle
  II they are rewritten and sharpened, not deleted.
- Bad, because the frontend's containment claim rests on behavioural tests plus deletion
  rather than on a structural scan, since no mechanism in this repository can scan
  TypeScript. This is an accepted, named limitation; a future ADR may revisit it if the
  frontend acquires architectural governance more broadly.
- Neutral, because no new port, assembly, package, or external system is introduced;
  Principle I's hexagonal gate does not fire, and ADR-010's containment rules C1–C5 are
  untouched.
- Neutral, because the two failures no retry can fix (`conversation_record_unreadable` from
  ADR-014; the restart decline for a missing normalized source from ADR-025) are handled by
  their catalogue `detail` naming the real recovery, rather than by adding a fifth category
  or a per-code retry flag.

## More Information

Detailed rationale: `specs/024-api-error-presentation/research.md` (R1–R6). Contract:
`specs/024-api-error-presentation/contracts/api-error-envelope.md`. Source issue:
[#85](https://github.com/wmalgadey/grimoire/issues/85).

Status: **accepted** by explicit author sign-off on 2026-08-16 — the project owner directed
the full spec-kit workflow for issue #85 to run to completion without further questions,
which is the sign-off Constitution Principle III names as the alternative to review. This
cleared the gate for `/speckit-tasks`. A reviewer who disagrees with any decision above
should record the change as a superseding or amending ADR rather than editing this one
(Principle III, "ADR Status Maintenance").
