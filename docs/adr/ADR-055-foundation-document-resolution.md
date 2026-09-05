---
status: proposed
---

# ADR-055: The Effective Foundation Document Is the Build-Distributed Default Unless an Instance Document Exists

> **Status notes** (informational, no status change):
> - Extends [ADR-043](ADR-043-build-distributed-agent-artifacts.md): the default copy is a new file in
>   each agent's build output, delivered by the mechanism that ADR already decided.
> - Extends [ADR-041](ADR-041-independent-directory-roots.md) and
>   [ADR-052](ADR-052-memory-directory-root.md): the instance document is a fixed, derived filename
>   under the existing data root; no new root and no new configuration key.
> - Related: [ADR-053](ADR-053-agent-system-prompt-composition.md), which decides that a foundation
>   document exists and how it composes, and [ADR-056](ADR-056-instance-instruction-custody.md), which
>   decides who may write the instance document.

## Context and Problem Statement

[ADR-053](ADR-053-agent-system-prompt-composition.md) makes a shared foundation document part of every
agent's system prompt, but says nothing about where that document comes from. Two requirements pull in
opposite directions.

A default must always be present: loading is fail-closed, so an installation that configures nothing
still has to find a foundation document. ADR-043 already guarantees exactly that property for every
other instruction document — the build delivers it into the agent directory, and a relocated agent
directory stays current across rebuilds.

But the point of the document is that an instance can say what *its* wiki is. In the container image the
agent directory is image content, rebuilt on every deployment: a file placed there by an operator is
gone at the next deploy, and cannot be put there at all without rebuilding. So the instance's own
statement cannot live where the default lives.

A fifth path root holding one shared copy was considered and does not resolve the tension either: the
default still has to *get* there, either by three agent builds racing on one shared write target or by
the Hub seeding the file at startup — which is the option ADR-043 rejected precisely because it makes
the Hub the author of instruction content (Constitution Principle V). A fresh deployment would mount an
empty volume and fail closed on first start.

## Decision Drivers

- A default must exist with zero configuration, in a location the build keeps current (ADR-043).
- An instance's own document must survive redeployment, rollback and restart of that instance.
- The writer of the instance document is the Hub itself (ADR-056), so the location must be one the Hub
  can write — which rules out image content and read-only mounts.
- A misresolution must not be silent; equally, no new way to misconfigure a deployment should be
  introduced (ADR-042's concern).
- Evaluation and replay runs must resolve the document without operator configuration (ADR-043).

## Considered Options

1. **Build-distributed default per agent, plus an instance document at a fixed, derived location under
   the existing data root.**
2. **Build-distributed default only** — no instance document; an instance customizes by editing the
   repository and rebuilding.
3. **A fifth path root** holding the single shared document, seeded by the build or by the Hub.
4. **A new configuration key naming the instance document's path**, defaulting to empty.
5. **The instance document inside the wiki root**, alongside the content it describes.

## Decision Outcome

Chosen option: **Option 1.**

- **Default**: one authored source document in the repository, delivered by each agent's own build into
  `<AgentDir>/<agentId>/Instructions/foundation-prompt.md` — a fixed, non-configurable filename inside
  the per-agent layout ADR-043 owns, exactly like `system-prompt.md` and `policy.json`. Each agent build
  writes only its own subfolder, so there is no shared write target and no parallel-build race. It is
  validated as a required input at startup, per agent, and fails fast when absent.
- **Instance document**: a single file at `<DataDir>/foundation-prompt.md` — a fixed filename derived
  from the existing data root in code, the same treatment `index.md`, `log.md` and the lint pid file
  already receive. It is optional: when the file exists it is the effective foundation document **for
  every agent**; when it does not, every agent resolves its own build-distributed copy.
- **Resolution is presence-based**, and deliberately so: no configuration names this location, so there
  is no path for an operator to mistype and no configured-but-missing case that could silently fall back
  to the default. The file is either there — written by the identity wizard (ADR-056) — or it is not.
- **No new configuration key, no new root, no new volume, no container change.** The data root is
  volume-backed in every deployment shape, which is what makes the instance document survive
  redeployment, rollback and restart.
- **Evaluation and replay** resolve the default from the agent project sources, exactly as they already
  resolve every other instruction document — no hub configuration, no prior agent build, and no data
  root, so an eval run always operates under the shipped default.
- **Resolution happens per run**, at the point the harness composes an agent's instruction paths. A run
  dispatched after the instance document changes operates under the new content; no restart is required,
  and a run already in flight keeps what it started with.
- **Which source is in effect is observable**: startup and each run report the resolved foundation
  document path and whether it came from the build-distributed default or the instance document, and
  each run additionally records the document's version (ADR-053).
- **Feature-Scoped Invariant** (Principle III): "with no instance document present every agent resolves
  its own build-distributed copy; with one present every agent resolves that same file". It pins this
  feature's current resolution surface rather than a dependency direction, and is verified by
  classicist behavioral tests — dispatch with and without the file present and assert the resolved
  paths and the reported source — never by reflecting over the options type or the configuration binder.
- This ADR introduces **no Boundary Rule**; who may write the instance document is decided in ADR-056.

### Consequences

- Good, because a zero-configuration installation and a specialised deployment are the same mechanism
  with one file's presence different, and neither requires the Hub to author instruction content.
- Good, because the deployment surface does not grow at all: no compose entry, no bind mount, no
  environment variable, nothing for an operator to get wrong.
- Good, because an instance's document lives in a volume that redeployment and rollback do not touch,
  and that an operator can reach with the tools they already use for the wiki.
- Bad, because the default document physically exists three times (once per agent directory) even
  though it is authored once. Accepted: they are build copies of a single source, produced by the same
  mechanism that already copies every other instruction file, and nothing can edit one of them
  independently without the next build overwriting it.
- Bad, because "which document is in effect" is now a question with two possible answers instead of one.
  Mitigated by reporting the effective path and its source at startup and per run.
- Bad, because an instruction document now lives under a root whose name says "data". Accepted: the
  alternatives are worse — the wiki root is agent-writable, which would let agents rewrite their own
  steering, and the memory root is scoped by ADR-052 to per-run bookkeeping records.
- Neutral, because evaluation runs can only ever exercise the shipped default. That is the correct
  behaviour for a hermetic suite, and an instance document is an operator artifact, not a test input.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new agent id whose build delivers its own copy of the
  default; additional reporting of the effective document and its source; a user-facing surface that
  displays or sets the instance document through the same mechanism.
- **Invalidations (would require full supersession):** per-agent instance documents (different agents
  steered by different foundation documents); a configuration key or precedence chain above the
  presence check; the instance document moving to a root that agents can write; the Hub seeding or
  authoring the default rather than reading what the build delivered.

## More Information

Read alongside [ADR-053](ADR-053-agent-system-prompt-composition.md), which decides that the foundation
document exists and how it is composed into the system prompt, and
[ADR-056](ADR-056-instance-instruction-custody.md), which decides that exactly one named custodian may
write the instance document and only with bytes it received whole.

A configured override key was the design until the identity wizard moved from the deployment script
into the Hub. Once the writer and the reader are the same process, a configured path buys nothing and
costs a validation branch and a new way to misconfigure a deployment; the reasoning is recorded in
`specs/029-shared-foundation-prompt/research.md` R1 and R6.
