---
status: accepted
---

# ADR-005: Observability Backend (Local and CI)

## Context and Problem Statement

Every piece of Grimoire's functionality is required to emit OpenTelemetry
instrumentation — traces, metrics, and structured logs — as a non-negotiable condition of
being considered done, so that agent behavior (LLM latency, tool calls, error rates) is
observable rather than invisible. This presupposes an OTel collector/backend, which has
not yet been chosen. The requirement must be verifiable both by a developer running
Grimoire locally, without deploying any cloud infrastructure, and in CI. No
instrumentation has been emitted by the project before, so the backend must be decided
now rather than assumed.

## Decision Drivers

- Must be verifiable locally without cloud infrastructure.
- Must be verifiable in CI without standing up long-lived infrastructure in the pipeline.
- Must stay proportional to a solo-developer project — the initial set of spans/metrics/
  logs emitted by any one feature is small and fixed, not high-volume.

## Considered Options

1. OpenTelemetry .NET SDK + OTLP export to the .NET Aspire Dashboard locally; in-memory
   exporter assertions in CI
2. Grafana + Tempo + Loki docker-compose stack, locally and in CI
3. No local backend; instrumentation only verified against a hosted/cloud APM

## Decision Outcome

Chosen option: **Option 1.**

The Hub and Ingest agent both use the OpenTelemetry .NET SDK, exporting via OTLP. For
local development, the target backend is the **.NET Aspire Dashboard** — a single local
container with a native OTLP receiver and trace/metric/log viewer, requiring no cloud
account or multi-service compose stack. In CI, instrumentation is verified with an
in-memory OTel exporter wired into integration tests (asserting the specific span names,
metric increments, and log events each feature declares it must emit), rather than
running any collector in the pipeline.

### Consequences

- Good, because it satisfies local-verifiability with a single container and no cloud
  dependency.
- Good, because CI verification via in-memory exporter assertions avoids any
  infrastructure dependency in the pipeline, keeping CI fast and hermetic.
- Neutral, because the Aspire Dashboard is a dev-time viewer only; a production/shared
  OTel backend (e.g. hosted APM) is an explicitly deferred decision for when Grimoire
  runs anywhere beyond the developer's own machine.

## More Information

This fixes the observability backend for the whole project; later features reuse it
rather than re-deciding it, and only need to declare their own specific spans, metrics,
and log events on top of it.
