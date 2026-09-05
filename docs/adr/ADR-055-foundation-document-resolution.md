---
status: proposed
---

# ADR-055: The Effective Foundation Document Is the Build-Distributed Default Unless One Configured Override Replaces It

> **Status notes** (informational, no status change):
> - Extends [ADR-043](ADR-043-build-distributed-agent-artifacts.md): the default copy is a new file in
>   each agent's build output, delivered by the mechanism that ADR already decided.
> - Extends [ADR-042](ADR-042-mandatory-configuration-file.md): one new configuration key, whose
>   default value lives in the mandatory configuration file and nowhere else — the extension its own
>   Change Triggers name.
> - Extends [ADR-041](ADR-041-independent-directory-roots.md): the override is an operator-supplied
>   file input alongside the roots, resolved the same way `SecretsFile` already is.
> - Related: [ADR-053](ADR-053-agent-system-prompt-composition.md), which decides that a foundation
>   document exists and how it composes; this ADR decides only where the effective one comes from.

## Context and Problem Statement

[ADR-053](ADR-053-agent-system-prompt-composition.md) makes a shared foundation document part of every
agent's system prompt, but says nothing about where that document comes from. Two requirements pull in
opposite directions.

A default must always be present: loading is fail-closed, so an installation that configures nothing
still has to find a foundation document. ADR-043 already guarantees exactly that property for every
other instruction document — the build delivers it into the agent directory, and a relocated agent
directory stays current across rebuilds.

But the point of the document is that an instance can say what *its* wiki is. In the container image
the agent directory is image content, rebuilt on every deployment: a file placed there by an operator
is gone at the next deploy, and cannot be put there at all without rebuilding. So the instance's own
statement cannot live where the default lives.

A fifth path root holding one shared copy was considered and does not resolve the tension either: the
default still has to *get* there, either by three agent builds racing on one shared write target or by
the Hub seeding the file at startup — which is the option ADR-043 rejected precisely because it makes
the Hub the author of instruction content (Constitution Principle V). A fresh deployment would mount
an empty volume and fail closed on first start.

## Decision Drivers

- A default must exist with zero configuration, in a location the build keeps current (ADR-043).
- An instance's own document must survive redeployment, rollback and restart of that instance.
- The Hub must never write, seed or template instruction content (Principle V; ADR-043's rejected
  Option 4).
- A misconfigured path must fail loudly, not silently fall back to the default (ADR-042's whole point).
- Evaluation and replay runs must resolve the document without operator configuration (ADR-043).

## Considered Options

1. **Build-distributed default per agent, plus one optional configured override file.**
2. **Build-distributed default only** — no override; an instance customizes by editing the repository
   and rebuilding.
3. **A fifth path root** holding the single shared document, seeded by the build or by the Hub.
4. **Presence-based override at a fixed path** — no configuration key; the file is used if it happens
   to exist.

## Decision Outcome

Chosen option: **Option 1.**

- **Default**: one authored source document in the repository, delivered by each agent's own build
  into `<AgentDir>/<agentId>/Instructions/foundation-prompt.md` — a fixed, non-configurable filename
  inside the per-agent layout ADR-043 owns, exactly like `system-prompt.md` and `policy.json`. Each
  agent build writes only its own subfolder, so there is no shared write target and no parallel-build
  race. It is validated as a required input at startup, per agent, and fails fast when absent.
- **Override**: one configuration key, `Grimoire:Paths:FoundationPromptFile`, shipped in
  `appsettings.json` with an empty value.
  - Empty ⇒ no instance override; every agent resolves its own build-distributed copy.
  - Non-empty ⇒ that one file is the effective foundation document **for every agent**, and it is a
    required input: it must resolve to an existing file or startup fails naming the key, the
    configured value and the resolved path. There is no silent fallback for a configured-but-missing
    path.
- **Evaluation and replay** resolve the default from the agent project sources, exactly as they
  already resolve every other instruction document — no hub configuration, no prior agent build.
- **Which source is in effect is observable**: startup reports, per agent, the resolved foundation
  document path and whether it came from the build-distributed default or an instance override; each
  run additionally records the document's SHA-256 (ADR-053).
- **Feature-Scoped Invariant** (Principle III): "an empty override key resolves every agent to its own
  build-distributed copy; a non-empty one resolves all agents to that file, and a non-empty key naming
  a missing file fails startup". It pins this feature's current configuration surface rather than a
  dependency direction, and is verified by classicist behavioral tests — start with the key empty, with
  it set, and with it set to a missing path, asserting the resolved paths and the documented failure —
  never by reflecting over the options type or the configuration binder.
- This ADR introduces **no Boundary Rule**; the instruction-authorship rule that keeps production code
  from writing these documents is owned by ADR-053.

### Consequences

- Good, because a zero-configuration installation and a specialised deployment are the same mechanism
  with one variable different, and neither requires the Hub to write instruction content.
- Good, because the override is the shape the codebase and its deployment documentation already
  explain for an operator-supplied file input (`SecretsFile`), rather than a new concept.
- Good, because an instance's document lives outside everything the deployment tooling rebuilds or
  checks out, so redeployment and rollback leave it alone.
- Bad, because the default document physically exists three times (once per agent directory) even
  though it is authored once. Accepted: they are build copies of a single source, produced by the same
  mechanism that already copies every other instruction file, and nothing can edit one of them
  independently without the next build overwriting it.
- Bad, because "which document is in effect" is now a question with two possible answers instead of
  one. Mitigated by reporting the effective path and its source at startup and recording its hash per
  run.
- Neutral, because a deployment that wants the override mounts one read-only file and sets one
  variable; nothing else about the container shape changes.

## Change Triggers

- **Extensions (do not invalidate this ADR):** a new agent id whose build delivers its own copy of the
  default; new deployment surfaces that set the same override key (a Kubernetes manifest, another
  compose overlay); additional reporting of the effective document and its source.
- **Invalidations (would require full supersession):** a second override tier or precedence chain
  above this key; per-agent override paths (different agents steered by different foundation
  documents); the override becoming presence-based rather than configured; the Hub, the CLI or any
  production code writing or seeding the document rather than reading it.

## More Information

Read alongside [ADR-053](ADR-053-agent-system-prompt-composition.md), which decides that the foundation
document exists and how it is composed into the system prompt.

The deployment-side mechanics that carry an operator's document into a container — one read-only bind
mount and one environment variable, both driven from the deployment's own `.env` — are deliberately not
fixed here. They are deployment configuration expressed in `compose.yaml` and
`deploy/server/grimoire-server`, not an architectural boundary, and this ADR constrains them only
through the key it defines.
