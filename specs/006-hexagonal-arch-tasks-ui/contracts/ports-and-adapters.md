# Contract: Hexagonal Ports & Adapter Containment (ADR-010)

**Feature**: 006-hexagonal-arch-tasks-ui

Normative structural contract enforced by `Grimoire.ArchTests` (NetArchTest), each rule
proven live by a Red/Green probe (Constitution Principles I & III).

## Ports (owned by consuming orchestration namespace)

| # | Port | Owner namespace | Production adapter | Adapter namespace | Test fake |
| --- | --- | --- | --- | --- | --- |
| P1 | `IAgentProcessLauncher` | `Grimoire.Hub.AgentDispatch` | `AgentProcessHost` | `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` | `FakeAgentProcess` |
| P2 | `IMarkdownConverter` | `Grimoire.Hub.IngestSubmission` | `MarkItDownConverter` | `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown` | `FakeMarkdownConverter` |
| P3 | `IUrlContentFetcher` | `Grimoire.Hub.IngestSubmission` | `UrlContentFetcher` | `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch` | `FakeUrlContentFetcher` |
| P4 | `IModelClient` | `Grimoire.IngestAgent.AgentCore` | `AnthropicModelClient` | `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic` | `FakeModelClient` |

Adapter namespaces nest under the consuming context (`<Consumer>.Adapters.<System>`,
ADR-010): each context's port, consumer, and adapter are co-located under one path.

## Containment rules (arch-test enforced)

| # | Rule |
| --- | --- |
| C1 | `Microsoft.Data.Sqlite` types referenced only from `Grimoire.Hub.OperationalState` (designated persistence adapter; persistence exemption — no port). |
| C2 | `Anthropic` SDK types referenced only from `Grimoire.IngestAgent.AgentCore.Adapters.Anthropic`. |
| C3 | `System.Net.Http` client usage in the Hub confined to `Grimoire.Hub.IngestSubmission.Adapters.HttpFetch` (composition root/telemetry wiring exempt). |
| C4 | `System.Diagnostics.Process` usage in the Hub confined to `Grimoire.Hub.AgentDispatch.Adapters.AgentProcess` and `Grimoire.Hub.IngestSubmission.Adapters.MarkItDown`. |
| C5 | Namespaces outside an `.Adapters.` segment must not reference `AgentProcessHost`, `MarkItDownConverter`, or `UrlContentFetcher` (Hub) or `AnthropicModelClient` (agent). Composition roots (`Program.cs`) exempt. |

**Rule-authoring note**: orchestration-side rules ("no infrastructure imports in
orchestration namespaces") MUST carve out `.Adapters.` sub-namespaces, since adapters
share their consumer's namespace prefix. The C1–C5 Red/Green probes verify the carve-out
leaves no enforcement hole.

## Exemptions

- **Persistence & local filesystem** (Constitution Principle I): repositories, artifact
  stores/writers, projection stores stay concrete — no ports, containment only (C1).
- **Composition root**: each process's `Program.cs` wires concrete adapters to ports.

## Verification protocol

For every rule C1–C5 and every port P1–P4: hermetic harness tests exercise orchestration
through the fake; a temporary deliberately-violating type proves the arch test fails
(Red), is deleted, and the test passes (Green). Probes never merge.
