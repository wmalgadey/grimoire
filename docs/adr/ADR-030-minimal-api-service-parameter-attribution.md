---
status: accepted
---

# ADR-030: Explicit `[FromServices]` Attribution for Minimal-API Handler Parameters

## Context and Problem Statement

ASP.NET Core's `RequestDelegateFactory` infers the binding source of a minimal-API
handler parameter it cannot otherwise place (no route/query name match, not one of the
special-cased framework types) by asking the host's `IServiceProviderIsService` whether
the parameter's type is DI-registered. If the answer is yes, the parameter is treated as
a service and resolved from the container — no attribute required. If the check cannot
confirm this, the parameter is instead inferred as `[FromBody]`. For a `GET`/`DELETE`
endpoint, an inferred body parameter is an immediate, unconditional failure at
route-matcher-build time ("Body was inferred but the method does not allow inferred body
parameters") — and it fires for **every host that maps the endpoint group**, not only for
requests that reach the handler, because the matcher is built once for the whole group.

This already cost a CI cycle (issue #56, PR #57): `GetDefaultsAsync` gained a
`ResolvedGrimoirePaths` parameter without `[FromServices]`. Production's composition root
(`HubHostComposition.BuildAsync`) registers `ResolvedGrimoirePaths` as a singleton
instance, so the inference happens to succeed there — but five *unrelated* test fixtures
that build a leaner Hub host for entirely different endpoints failed to build their route
matcher at all, because their composition doesn't register the same type the same way.
Twelve tests broke, none of which call `/defaults`. The inference's correctness depends on
exactly which host composes the container at that moment — production, or any of several
independent test compositions — which makes it not just a build-time convenience but a
correctness question that varies per test fixture, not per handler.

Today the codebase contains two `[FromServices]` occurrences, both added reactively after
a failure was already observed. There is no convention document and no CI gate, so the
next handler that takes a service parameter repeats the same failure with the same
misleading blast radius (Constitution Principle IV: "Conventions not enforced by CI/CD do
not exist").

## Decision Drivers

- Constitution Principle IV: every architectural/quality rule needs a CI/CD gate that
  fails the build on violation — a convention doc alone does not satisfy this.
- Constitution Principle III: a new cross-cutting rule needs an ADR before its structural
  test is written, and the ADR must classify the rule as a Boundary Rule (Phase 0,
  reflection/IL-based, Red/Green probed) or a Feature-Scoped Invariant (classicist
  behavioral test).
- Constitution Principle II ("Test what we own"): the rule must assert a fact our own
  source code decides (attribute presence on our handler parameters), not re-verify
  ASP.NET Core's own inference logic.
- Blast radius: the failure mode is a route-matcher-build-time exception that can surface
  in any host — production or any test fixture — that maps the affected group, regardless
  of whether that host's test even exercises the affected endpoint.

## Considered Options

1. **Structural rule requiring `[FromServices]` (or another explicit binding attribute) on
   every minimal-API handler parameter that isn't a recognized route/query/framework
   type**, enforced by a Mono.Cecil-based `Grimoire.ArchTests` rule that discovers actual
   handler delegates (methods reached by an `ldftn` from a `Map*` route-group/app mapping
   method) rather than guessing by name or return type.
2. Prose convention only, relying on code review to catch missing attributes (rejected —
   Principle IV forbids treating an unenforced convention as binding; this is exactly how
   the codebase arrived at issue #56/#57 in the first place).
3. A wrapper/analyzer that fails the *build* (a Roslyn analyzer) instead of a test
   (rejected for now — no existing Roslyn analyzer package is wired into this repo's
   build, whereas `Grimoire.ArchTests` + Mono.Cecil is the established mechanism for every
   comparable structural rule; a future ADR may add analyzer tooling, but introducing a
   new toolchain to enforce one rule is disproportionate here).
4. Require the DI container itself to be probed at test time (e.g., build a
   `WebApplicationFactory` per composition and assert route-matcher construction
   succeeds) instead of a static rule (rejected as the *only* mechanism — it would only
   catch a violation in whichever compositions the test suite happens to boot with the
   affected group mapped, reproducing exactly the "some hosts break, others don't"
   blast-radius problem this ADR exists to close; a static rule catches it for every host,
   including ones not yet written).

## Decision Outcome

Chosen option: **Option 1.**

### Mechanism

- New Boundary Rule test: `Grimoire.ArchTests/MinimalApiServiceParameterRuleTests.cs`.
- **Boundary Rule** (Constitution Principle III classification): this rule concerns the
  minimal-API surface's parameter-attribution *shape*, held durable regardless of how many
  handlers or endpoint families the Hub grows to — adding a new handler never requires
  editing the rule itself, the same low-maintenance property Principle III reserves for
  Boundary Rules (contrast with a Feature-Scoped Invariant like "the CLI exposes exactly N
  switches", which is expected to change as that surface grows). Phase 0, reflection/IL
  based, Red/Green probed.
- **Handler discovery**: every `Grimoire.Hub` method reached by an `ldftn` instruction
  inside a `Map*(RouteGroupBuilder, ...)` / `Map*(WebApplication, ...)` mapping method —
  the exact delegate registered with `MapGet`/`MapPost`/etc. — not a naming or
  return-type heuristic over handler methods themselves. This means a private helper that
  merely looks like a handler is never mistaken for one, and a handler registered under an
  unconventional name is never missed.
- **Exempt parameter types** (never require an explicit attribute — bound from
  route/query/framework by construction, mirrored in
  `docs/conventions/minimal-api-service-parameters.md`): `string`, `bool`, `int`, `long`,
  `Guid` (and their nullable forms), `CancellationToken`, `HttpRequest`, `HttpContext`,
  `HttpResponse`, `IFormFile`, `IFormFileCollection`, `ILoggerFactory`,
  `ClaimsPrincipal`, `Stream`.
- **Exempt by convention**: a parameter type whose name ends in `Request` — the project's
  existing naming convention for body DTOs (`UrlSubmissionRequest`,
  `QueryTurnSubmissionRequest`, `RemediationAttachContextRequest`,
  `RemediationSendMessageRequest`), none of which are DI-registered, so the implicit
  `[FromBody]` inference is correct for them by construction.
- **Everything else** — every parameter whose type is none of the above — must carry an
  explicit ASP.NET Core binding attribute: `[FromServices]` (the expected case for a
  handler's DI collaborators), or `[FromBody]`/`[FromRoute]`/`[FromQuery]`/
  `[FromHeader]`/`[FromForm]`/`[AsParameters]` where one of those is the correct explicit
  choice instead.
- Applied retroactively to close the gap the rule finds: every pre-existing handler
  parameter that was relying on the inference happening to succeed (`store`,
  `contentPaths`, `coordinator`, `recordStore`, `stateRepository`,
  `transitionService`, `messageTurnCoordinator`, `validator`, `pipeline`,
  `readModel`, `sourceArtifactStore`, and equivalents across the ingest, query, lint, and
  remediation endpoint families) now carries `[FromServices]` explicitly.
- Red/Green probe (Constitution Principle III): a temporary unannotated
  `LintRunCoordinator coordinator` parameter added to
  `LintSubmissionEndpoints.GetLatestAsync` was confirmed to fail the rule, then removed.

### Structural enforcement (Constitution III)

No new namespace or assembly. The rule lives in the existing `Grimoire.ArchTests`
project, using the same Mono.Cecil IL-scan idiom `ArchScan.cs` already establishes for
call-site analysis elsewhere in that project. It runs in the standard PR pipeline
alongside every other ArchTests rule.

### Consequences

- Good, because the class of failure that broke #56/#57 (an inference that silently
  depends on which host composes the container) becomes a compile-adjacent, deterministic
  CI failure instead of a runtime surprise discovered by an unrelated test fixture.
- Good, because the rule is durable across growth: a new handler with a new DI
  collaborator is covered automatically, with no edit to the rule itself required.
- Good, because handler discovery follows the actual delegate registration (`ldftn` from a
  `Map*` method) rather than a naming heuristic, so it cannot miss an unconventionally
  named handler or false-positive on a same-shaped helper method.
- Bad, because the `*Request` naming exemption is a convention, not a structural
  guarantee — a body DTO that doesn't follow the naming convention would need an explicit
  `[FromBody]` instead (already true today, and the rule enforces exactly that: no
  silent gap, just a slightly more explicit spelling required for the non-conforming
  case).
- Bad, because the exempt-framework-type list is a maintained allowlist, not derived from
  the real DI container — a new well-known ASP.NET Core framework type would need adding
  to the list once, in the same low-maintenance shape as the naming convention above.

## More Information

Surfaced by `/speckit-analyze` on `specs/021-ingest-content-paths` (finding F6);
formalized as issue #59. `docs/conventions/minimal-api-service-parameters.md` records the
convention this ADR's rule enforces, in the same format as
`docs/conventions/agent-artifact-naming.md` (ADR-013).
