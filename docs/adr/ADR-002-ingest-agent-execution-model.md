---
status: accepted
---

# ADR-002: Ingest Agent Execution Model

## Context and Problem Statement

Grimoire's Hub dispatches work to specialized agents (Ingest, Query, Lint). Most agents
can run safely in-process as ordinary library calls, but the Ingest agent is different: it
runs its own LLM-based processing pipeline (reading a source, deciding how to update the
wiki, and writing results), which brings its own runtime and dependency chain, and which
the project wants to be able to run and scale independently of the Hub — including,
eventually, in its own container. This decision fixes how the Hub and the Ingest agent are
wired together, since no agent has previously been implemented in this project.

## Decision Drivers

- Ingest must be independently containerizable later without a rewrite of its interface
  to the Hub.
- Operational overhead must stay proportional to a solo-developer project — no message
  broker or job-queue service should be introduced to support what is currently a single
  concurrent ingest operation at a time.
- A failed ingest must never leave partial or orphaned content in the wiki, and if the
  Hub restarts while an ingest is in progress, the interrupted operation must be
  reconcilable to a clearly failed state rather than being left stuck as "running"
  forever.

## Considered Options

1. Ingest agent as a standalone .NET console app, invoked by the Hub as a child process
2. Ingest agent as an in-process library called directly by the Hub
3. Ingest agent as a separately-running daemon, called over HTTP/gRPC

## Decision Outcome

Chosen option: **Option 1 — standalone console app, invoked as a child process.**

The Hub spawns the Ingest agent executable per submission, passing the source reference
and repository paths (wiki, tasks, index, log locations) via CLI arguments and a scoped
environment (see ADR-004 for credential scoping). The agent performs all reads/writes
against the git working tree directly and exits with a status code; it is responsible for
creating and updating its own task artifact — a persistent file recording what it did —
throughout its lifecycle, from the moment it starts to its final success or failure. The
Hub does not hold ingest business logic in-process.

### Consequences

- Good, because the CLI contract (arguments in, task artifact + wiki files + exit code
  out) is the same contract a future container would use — containerizing later is a
  deployment change, not a redesign.
- Good, because a crashed/killed subprocess leaves no in-process Hub state to corrupt;
  restart reconciliation only needs to check whether a task file is still "running" with
  no live process, which the Hub's operational-state store (ADR-003) already tracks.
- Bad, because subprocess invocation has no built-in retry/backoff or queueing; acceptable
  while there is only ever one trusted operator submitting one ingest at a time.
- Neutral, because this defers (not rejects) daemon/queue-based execution — Option 3
  remains available once concurrent ingest volume justifies its operational cost.

## More Information

This ADR establishes the general pattern for any agent whose processing pipeline needs a
lifecycle independent of the Hub: standalone executable, spawned as a child process, with
a file-based result contract. Query and Lint may or may not need the same treatment —
that is a decision for whichever ADR introduces them, not assumed here.
