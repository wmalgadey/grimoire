---
status: proposed
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

1. **Per-system adapter namespaces** `Grimoire.<Process>.Adapters.<System>`, ports stay in
   their consuming orchestration namespace, composition root exempt, NetArchTest
   containment rules
2. Separate adapter assemblies (one project per adapter) with compile-time-enforced
   dependency direction
3. Single flat `Adapters` namespace per process without per-system segmentation

## Decision Outcome

Chosen option: **Option 1.**

### Ports and adapters

| Port | Owner (consumer) namespace | Production adapter → adapter namespace | Test fake |
| --- | --- | --- | --- |
| `IAgentProcessLauncher` | `Grimoire.Hub.AgentDispatch` | `AgentProcessHost` → `Grimoire.Hub.Adapters.AgentProcess` | `FakeAgentProcess` |
| `IMarkdownConverter` (new) | `Grimoire.Hub.IngestSubmission` | `MarkItDownConverter` → `Grimoire.Hub.Adapters.MarkItDown` | `FakeMarkdownConverter` |
| `IUrlContentFetcher` (new) | `Grimoire.Hub.IngestSubmission` | `UrlContentFetcher` → `Grimoire.Hub.Adapters.HttpFetch` | `FakeUrlContentFetcher` |
| `IModelClient` | `Grimoire.IngestAgent.AgentCore` | `AnthropicModelClient` → `Grimoire.IngestAgent.Adapters.Anthropic` | `FakeModelClient` |

### Containment rules (enforced in `Grimoire.ArchTests`, each with a Red/Green probe)

- C1: `Microsoft.Data.Sqlite` only in `Grimoire.Hub.OperationalState` (designated
  persistence adapter namespace).
- C2: `Anthropic` SDK only in `Grimoire.IngestAgent.Adapters.Anthropic`.
- C3: outbound HTTP client usage in the Hub only in `Grimoire.Hub.Adapters.HttpFetch`.
- C4: process spawning (`System.Diagnostics.Process`) in the Hub only in
  `Grimoire.Hub.Adapters.AgentProcess` and `Grimoire.Hub.Adapters.MarkItDown`.
- C5: non-adapter namespaces never reference concrete adapter types.

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

- Good: hermetic tests replace every external system through a port; contributors get a
  single obvious location per adapter; violations fail CI with a named rule.
- Good: no new assemblies, no build restructuring; pure namespace moves + two interface
  extractions, so behavior and contracts are untouched.
- Bad: `git blame` continuity breaks for the moved files; mitigated by move-only commits.
- Bad: five more arch rules to maintain; accepted — they are the enforcement the
  constitution demands.

## Verification

- `Grimoire.ArchTests` gains rules C1–C5 plus port-presence checks; the standard PR
  pipeline runs them (Principle IV).
- Each rule's Red/Green probe is executed during Phase 0 of feature 006 and documented in
  its tasks.md; probes are never merged.
