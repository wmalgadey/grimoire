# Phase 0 Research: Rename ContentRootPaths to an Ingest-Specific Type

## Approach

No research agents were dispatched. The Technical Context in `plan.md` contains zero
`NEEDS CLARIFICATION` markers: language/framework, testing framework, project structure,
and full call-site scope were all resolved by direct inspection of the existing
codebase (`grep`/`Read`) during specification, not by inference or industry-standard
defaults. This is a same-repository, same-language, same-test-framework mechanical
rename — there is no new technology, library, or pattern to evaluate.

The findings below record what was confirmed by direct inspection, in the Decision /
Rationale / Alternatives format, for traceability.

## R1 — Confirming `ContentRootPaths` is Ingest-only in practice

**Decision**: Treat `ContentRootPaths` as Ingest-owned for the purpose of rule N1,
matching the source issue's premise.

**Rationale**: A repository-wide search for the identifier confirms every production
consumer is Ingest-scoped: `SubmissionService`, `IngestSubmissionPipeline`,
`IngestRunCoordinator`, `IngestSubmissionEndpoints`, `BoardEndpoints` (board composite
reads for ingest tasks), and the ingest CLI commands (`SubmitSourceCommand`,
`IngestRetriggerCommand`, `IngestResumeCommand`). No Query or Lint code references it —
those agents' `LintRunCoordinator`/`QueryRunCoordinator` already carry their own
tokens and do not consume this type.

**Alternatives considered**: Moving the type into an Ingest-owned namespace
(`Grimoire.Hub.IngestSubmission`) instead of renaming in place. Rejected: the ADR-013
namespace-ownership map already classifies `Grimoire.Hub.ContentRoot` as cross-agent and
explicitly permits cross-agent namespaces to host per-agent-named types of shared
infrastructure (mirroring `Realtime`'s `IngestLifecycleHub`, `Cli`'s `LintRunCommand`).
The N1 architecture test's strict "no agent-token type in a cross-agent namespace" rule
applies only to `Grimoire.Hub.AgentDispatch`, not to `ContentRoot` — confirmed by reading
`AgentArtifactNamingRuleTests.cs` directly. A namespace move is therefore unnecessary
surface area the issue itself does not ask for.

## R2 — Confirming the duplicated fields have a single authoritative source

**Decision**: `ResolvedGrimoirePaths.Ingest` (`AgentRuntimePaths`) is the sole
post-change source for `SystemPromptPath`, `DefaultUserPromptPath`, and `PolicyPath`.

**Rationale**: `ContentRootPaths.FromResolved` already populates its three instruction
fields directly from `resolved.Ingest.{SystemPromptPath,DefaultUserPromptPath,PolicyPath}`
— confirmed by reading `ContentRootPaths.cs`. The values are never independently
computed; the projection is a pure pass-through. Removing the fields and pointing
callers at `ResolvedGrimoirePaths.Ingest` directly changes no resolved value (spec
FR-009/SC-006), only which type callers ask.

**Alternatives considered**: Keeping the fields but marking them `[Obsolete]` for a
deprecation window. Rejected: the constitution's Big-Design-Up-Front rejection and the
project's pre-1.0, no-external-consumers posture (established for the analogous
ADR-022 switch removal) both favor a clean removal over a compatibility shim with no
external consumer to protect.

## R3 — Confirming exact renamed-type name and file scope

**Decision**: `ContentRootPaths` → `IngestContentPaths`; file
`ContentRootPaths.cs` → `IngestContentPaths.cs`, one type per file (existing repo
convention, confirmed by inspecting the `ContentRoot/` folder).

**Rationale**: `IngestContentPaths` is the exact name the source issue proposes and
reads unambiguously as "Ingest" + "Content" + "Paths", consistent with the pattern
`LintRunCoordinator`/`QueryRunCoordinator` already establish (agent token first).

**Alternatives considered**: `IngestWikiPaths` (echoing the `ContentRoot` → `WikiDir`
rename ADR-022/R9 already made at the configuration-option level). Rejected in favor of
staying with the issue's own proposed name — the type's remaining fields
(`TasksDir`, `IndexPath`, `LogPath`, `WriteLocksDir`) are not exclusively "wiki"
locations (`WriteLocksDir` is runtime-state, not wiki content), so `Content` remains the
more accurate umbrella term than `Wiki`; this matches the type's own doc comment, which
already describes it as "Wiki root and Ingest agent-instruction locations".

## Outcome

All Technical Context fields in `plan.md` are resolved with no outstanding unknowns.
Proceeding directly to Phase 1 design.
