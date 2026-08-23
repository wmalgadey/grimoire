# Minimal-API Handler Service-Parameter Attribution

**Status**: Active (established by issue #59)
**Enforced by**: `backend/tests/Grimoire.ArchTests/MinimalApiServiceParameterRuleTests.cs`
(Red/Green probed, standard PR pipeline)

## Rule

Every minimal-API handler parameter in `Grimoire.Hub` — a parameter of a method actually
registered as a route delegate via `MapGet`/`MapPost`/`MapPut`/`MapDelete`/etc. — must be
unambiguous about where it comes from. A parameter is compliant if it is:

1. one of the recognized route/query/framework types (below), which the minimal-API
   pipeline always binds from the route, query string, or framework surface, never the DI
   container; or
2. a request-body DTO whose type name ends in `Request` (the project's existing naming
   convention for these types — `UrlSubmissionRequest`, `QueryTurnSubmissionRequest`,
   `RemediationAttachContextRequest`, `RemediationSendMessageRequest`); or
3. explicitly annotated with an ASP.NET Core binding attribute — `[FromServices]` for a DI
   collaborator (the expected case), or `[FromBody]`/`[FromRoute]`/`[FromQuery]`/
   `[FromHeader]`/`[FromForm]`/`[AsParameters]` where that is the correct explicit choice.

Everything else — in practice, every handler's DI-resolved collaborator (a coordinator, a
store, a validator, a repository, a path-configuration object, ...) — needs `[FromServices]`.

## Rationale

ASP.NET Core's `RequestDelegateFactory` infers an unannotated complex-type parameter's
binding source by asking the *current host's* `IServiceProviderIsService` whether the
type is DI-registered. If that check cannot confirm it, the parameter is inferred as
`[FromBody]` instead — and on a `GET`/`DELETE` handler, that is an immediate,
route-matcher-build-time failure that fires for **every host that maps the endpoint
group**, not just requests that reach the handler.

The inference's correctness therefore depends on exactly which host composes the DI
container at that moment. Production's composition (`HubHostComposition.BuildAsync`)
registers most Hub collaborators as singletons, so the inference usually succeeds there —
but a leaner test host built for an unrelated endpoint may not register the same type the
same way, and fails to build its route matcher at all. Issue #56/PR #57 hit exactly this:
`GetDefaultsAsync` gained a `ResolvedGrimoirePaths` parameter without `[FromServices]` and
broke 12 tests across 5 fixtures, none of which called `/defaults`.

Rather than relying on the inference succeeding by accident in every host that will ever
exist, this convention makes the binding source explicit on every handler parameter.

**Why a structural test, not just this document.** Constitution Principle IV: "conventions
not enforced by CI/CD do not exist." A prose-only version of this rule is exactly what was
already violated once (#56/#57 — no doc, no gate, no CI failure until an unrelated test
fixture broke). The alternative of asserting this via a live DI-container probe (e.g. a
`WebApplicationFactory` per composition, asserting route-matcher construction succeeds) was
considered and rejected: it would only catch a violation in whichever compositions the test
suite happens to boot with the affected group mapped, reproducing the same "breaks in some
hosts, not others" problem this convention exists to close. A static structural scan over
the actual registered handler delegates catches it for every host, including ones not yet
written — which is why `MinimalApiServiceParameterRuleTests` uses the same Mono.Cecil
IL-scan approach as the rest of `Grimoire.ArchTests` rather than a runtime check.

## Exempt route/query/framework types

Mirrored by hand in the rule's `_exemptParameterTypeFullNames` fixture — there is no
automated assertion checking the two stay in sync (see "Adding a new well-known framework
type" below); keep them consistent when editing either.

| Type | Why it's exempt |
| --- | --- |
| `string`, `bool`, `int`, `long`, `Guid` (and nullable forms) | Bound from the route or query string by name — never a DI service type. |
| `CancellationToken` | Framework-injected request-abort token, special-cased by the pipeline. |
| `HttpRequest`, `HttpContext`, `HttpResponse` | Framework-injected ambient request objects, special-cased by the pipeline. |
| `IFormFile`, `IFormFileCollection` | Bound from the multipart form body, special-cased by the pipeline. |
| `ILoggerFactory` | Part of every ASP.NET Core host's built-in logging services regardless of composition — always present, so the inference never varies by host. |
| `ClaimsPrincipal` | Framework-injected ambient identity, special-cased by the pipeline. |
| `Stream` | Framework-injected request body stream, special-cased by the pipeline. |

## Example

```csharp
// Before (relies on the pipeline's DI inference succeeding — silently host-dependent):
private static async Task<IResult> GetBoardAsync(
    KanbanBoardProjectionStore store, IngestContentPaths contentPaths,
    IngestRunCoordinator coordinator, CancellationToken cancellationToken)

// After (explicit — correct in every host, by construction):
private static async Task<IResult> GetBoardAsync(
    [FromServices] KanbanBoardProjectionStore store,
    [FromServices] IngestContentPaths contentPaths,
    [FromServices] IngestRunCoordinator coordinator,
    CancellationToken cancellationToken)
```

## Adding a new well-known framework type

If a future handler takes a parameter of a framework type not yet in the exempt list
above (e.g. a new ASP.NET Core built-in), add it to both this table and the rule's
`_exemptParameterTypeFullNames` fixture in the same change — the two are checked to stay
in sync only by this document's own instruction, not by an automated mirror assertion
(unlike the agent-artifact-naming exemption table, this list changes rarely enough that a
mirror assertion would add ceremony without a corresponding drift risk).
