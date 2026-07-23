---
status: accepted
---

# ADR-010: Hexagonal Ports and Adapter Namespaces for External Systems

## Context and Problem Statement

Constitution v1.4.0 amended Principle I with normative ports-and-adapters rules: every
external system that hermetic harness tests must replace (LLM provider API, spawned agent
processes, subprocess-based converters, outbound network fetching) must be consumed
through a port, ports are owned by the consuming orchestration namespace, and
infrastructure packages must be confined to designated adapter namespaces enforced by
structural tests. The codebase predates the amendment and conforms only partially:
`IModelClient` and `IAgentProcessLauncher` exist as ports, but their production adapters
live inside orchestration namespaces, `SubmissionService` references the concrete
`AgentProcessHost`, and the MarkItDown converter and URL fetcher have no ports at all.
The concrete namespace scheme, port inventory, containment rules, and exemptions are a
cross-cutting structural boundary no existing ADR covers, so they are fixed here
(feature 006-hexagonal-arch-tasks-ui).

## Decision Drivers

- Constitution Principle I: ports for replaceable external systems, port ownership on the
  consuming side, adapter containment, persistence exemption; namespace-level containment
  is sufficient — no extra assemblies (Big Design Up Front rejected).
- Principle II: hermetic harness tests must exercise orchestration through fakes
  implementing the same ports as production adapters.
- Principle III: every rule needs an automated structural test proven by a Red/Green
  probe; an ADR without enforcement must not be cited as a constraint.
- Zero behavioral change: the alignment must not alter any existing contract
  (ADR-002/006/007/008/009 remain in force).

## Considered Options

1. **Context-nested adapter namespaces** `<ConsumerNamespace>.Adapters.<System>` — each
   adapter lives directly beneath the orchestration namespace that owns its port
   (screaming/feature-first layout: port, consumer, and adapter co-located per bounded
   context), composition root exempt, NetArchTest containment rules
2. Per-process top-level adapter namespaces `Grimoire.<Process>.Adapters.<System>` —
   technical grouping in a shared adapter bucket, separated from the consuming contexts
3. Separate adapter assemblies (one project per adapter) with compile-time-enforced
   dependency direction
4. Single flat `Adapters` namespace per process without per-system segmentation

## Decision Outcome

Chosen option: **Option 1.** The hexagon is drawn per bounded context, not per technical
layer: an adapter is part of its context's boundary, so it lives one namespace below its
port and consumer. Option 2 was rejected because it scatters a context's functionality
across distant paths (a technical grouping contradicting the feature-first layout the
existing capability namespaces already establish) while offering no additional
enforceability.

### Ports and adapters

| Port | Owner (consumer) namespace | Production adapter → adapter namespace | Test fake |
| --- | --- | --- | --- |
| `IAgentProcessLauncher` | `Grimoire.Hub.AgentDispatch` | `AgentProcessHost` → `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` | `FakeAgentProcess` |
| `IMarkdownConverter` (new) | `Grimoire.Hub.IngestSubmission` | `MarkItDownConverter` → `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown` | `FakeMarkdownConverter` |
| `IUrlContentFetcher` (new) | `Grimoire.Hub.IngestSubmission` | `UrlContentFetcher` → `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch` | `FakeUrlContentFetcher` |
| `IModelClient` | `Grimoire.IngestAgent.AgentCore` | `AnthropicModelClient` → `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic` | `FakeModelClient` |

> This `IModelClient` row was superseded by ADR-011's "Hexagonal ports and containment
> (amends ADR-010)" section (008-query-agent): the port and its Anthropic adapter moved
> to `Grimoire.AgentRuntime.Core`/`Grimoire.AgentRuntime.Core.Adapters.Anthropic` when the
> Ingest/Query shared runtime was extracted. See ADR-011 for the current table entry.

### Containment rules (enforced in `Grimoire.ArchTests`, each with a Red/Green probe)

- C1: `Microsoft.Data.Sqlite` only in `Grimoire.Hub.OperationalState` (designated
  persistence adapter namespace).
- C2: `Anthropic` SDK only in `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic`.
- C3: outbound HTTP client usage in the Hub only in
  `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch`.
- C4: process spawning (`System.Diagnostics.Process`) in the Hub only in
  `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` and
  `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown`.
- C5: namespaces outside an `.Adapters.` segment never reference concrete adapter types.

**Rule-authoring note**: because adapter namespaces nest under their consumer's prefix,
orchestration-side rules ("orchestration namespaces must not import infrastructure
packages") MUST carve out `.Adapters.` sub-namespaces (e.g.
`ResideInNamespace("Grimoire.Hub.IngestSubmission").And().DoNotResideInNamespaceContaining(".Adapters")`).
The Red/Green probes for C1–C5 prove the carve-out leaves no enforcement hole.

### Exemptions

- **Persistence & local filesystem** (constitutional): repositories, artifact
  stores/writers, and projection stores remain concrete classes — introducing ports for
  them solely to enable mocking would violate Principle II. They remain subject to
  containment (C1).
- **Composition root**: each process's `Program.cs` (and its DI wiring) is the single
  place that constructs concrete adapters and binds them to ports. Framework/telemetry
  wiring (`TelemetryExtensions`, SignalR registration) counts as composition-root
  configuration.

New external systems added later must extend this table via their own ADR before
implementation (Principle I "new boundaries via ADR").

### Consequences

- Good: hermetic tests replace every external system through a port; each context's
  port, consumer, and adapter are co-located under one path (screaming architecture);
  violations fail CI with a named rule.
- Good: no new assemblies, no build restructuring; pure namespace moves + two interface
  extractions, so behavior and contracts are untouched.
- Bad: `git blame` continuity breaks for the moved files; mitigated by move-only commits.
- Bad: five more arch rules to maintain, and prefix-based orchestration rules need
  explicit `.Adapters.` carve-outs; accepted — they are the enforcement the constitution
  demands, and the probes verify the carve-outs.
- Neutral: the scheme assumes one consuming context per adapter (true for all four
  today); an adapter later shared by two contexts triggers the "new boundaries via ADR"
  path to pick or promote an owner.

## Verification

- `Grimoire.ArchTests` gains rules C1–C5 plus port-presence checks
  (`HexagonalPortsAdapterRuleTests`); the standard PR pipeline runs them (Principle IV) —
  24/24 `Grimoire.ArchTests` pass with zero active violations as of feature 006 completion.
- Each rule's Red/Green probe was executed and documented in feature 006's tasks.md
  commit history; no probe file was merged:
  - C1 (T001, Phase 0): probed immediately — no active violation existed.
  - C2, C3, C4, C5 (Hub and Agent), and port ownership (T018, User Story 1): probed after
    the move-only restructuring turned every rule Green.
- C2–C5 and the port-presence/ownership checks were genuinely Red against the
  pre-restructuring codebase (documented in the T001–T002 commit) before User Story 1
  turned them Green.
