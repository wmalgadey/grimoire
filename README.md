# Grimoire

![Code complexity](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/wmalgadey/grimoire/main/docs/metrics/complexity-badge.json)
![Estimated time to understand](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/wmalgadey/grimoire/main/docs/metrics/understanding-time-badge.json)

Grimoire is an LLM harness for a **Compound LLM-Wiki**: a personal knowledge base maintained
by LLM agents rather than by hand. Agents (Ingest, Query, and more to come) read raw sources,
maintain a structured markdown wiki, and answer questions over it — while a deterministic
backend harness owns dispatch, credential scoping, guardrails, and observability around them.

The two badges above give a rough, order-of-magnitude read on the codebase: average
cyclomatic complexity per function (`backend/src` + `frontend/src`, rated Low/Moderate/High/
Very High), and a heuristic estimate of how long a single engineer would need to read
through it once at a careful pace. See
[`docs/codebase-complexity-metric.md`](docs/codebase-complexity-metric.md) for the exact
formulas, thresholds, and sources.

## Getting Started

Grimoire ships as a self-hosted stack behind one Compose project — the Hub, the three
agent runtimes, the frontend, and a local telemetry dashboard. The full setup (credentials,
the `.env` configuration surface, reaching the stack from another machine) lives in
[`deploy/server/README.md`](deploy/server/README.md); this is the short path through it.

### Install and deploy

```bash
git clone https://github.com/wmalgadey/grimoire.git ~/grimoire
cd ~/grimoire
cp .env-example .env && chmod 600 .env && $EDITOR .env   # put a real ANTHROPIC_AUTH_TOKEN in it

./deploy/server/grimoire-server install       # copies the tool outside the checkout
export PATH="$HOME/.local/bin:$PATH"
export GRIMOIRE_REPO=$HOME/grimoire

grimoire-server deploy main                   # builds the images, brings the stack up, smoke-checks it
```

The first build is slow — the .NET SDK, Bun, and all three agent builds run — and every
later one reuses the layer cache. `grimoire-server status` reports what is deployed, what
origin has since, and whether the stack is actually serving; run it any time instead of
guessing. The stack publishes to `127.0.0.1` only — reach it from elsewhere over an SSH
tunnel or Tailscale, both covered in `deploy/server/README.md`.

### Point it at your own wiki

By default, every agent maintains the wiki Grimoire ships: a general-purpose personal
knowledge base. Check which one this instance is actually running:

```bash
grimoire-server wiki-identity
```

To have it maintain something else instead, ask the wizard for a drafting brief built from
your own description:

```bash
grimoire-server wiki-identity set --specialised --description "Tracks nothing but home-lab Kubernetes runbooks."
```

The wizard only reports and persists — it never drafts the document itself, since deciding
what a wiki should be is agent judgment, not something the deterministic harness is allowed
to do. Drafting is a job for whoever reads the brief, typically the Claude Code session
already running on the deploy host: the
[`wiki-identity-drafting`](.claude/skills/wiki-identity-drafting/SKILL.md) skill covers
exactly what a drafted document has to contain and how to hand it back with
`grimoire-server wiki-identity set --from-file <path>` — no restart needed, the next
Ingest, Query, or Lint run operates under it.

## Architecture

- **Backend** (`backend/`) — .NET / C#, hexagonal (ports & adapters) architecture
- **Frontend** (`frontend/`) — SvelteKit
- **Agents** (`backend/src/Grimoire.*Agent/Instructions/`) — per-agent instruction files (`system-prompt.md`, `policy.json`, plus `default-user-prompt.md` where the agent has a default steering message) that govern each agent's behavior at runtime, delivered to the configured agent directory by the agent build. Every agent additionally loads one shared foundation document ([ADR-053](docs/adr/ADR-053-agent-system-prompt-composition.md)) ahead of its own `system-prompt.md`; an operator can point an instance at a specialised one instead of the shipped default via the Hub's `wiki-identity` command
- **Evals** (`backend/tests/Grimoire.EvalRunner`, `backend/tests/Grimoire.AgentEvals`) — a standalone runner that replays committed recordings at the model port ([ADR-012](docs/adr/ADR-012-eval-runner-recorded-replay.md)), so agent *judgment* is scored against thresholds rather than pinned by deterministic tests

Grimoire is split into a **deterministic harness** (the Hub: dispatch, credential scoping,
guardrails, persistence, observability) and the **agents** that actually decide what the
wiki says. The harness never decides wiki content, and the agents never write outside the
guarded tool layer — that boundary is the whole design.

Inside the backend, the hexagon is drawn **per bounded context, not per technical layer**
([ADR-010](docs/adr/ADR-010-hexagonal-ports-adapter-namespaces.md)). A context owns its
port interface, and the adapter that implements it sits one namespace below, so port,
consumer, and infrastructure are read together instead of scattered across a shared
`Adapters` bucket. Concretely, three rings:

- **Domain core** — `backend/src/Grimoire.Domain` — dependency-free by construction: safety
  policy and its decisions, ingest submission kinds. It imports no framework, no
  infrastructure, nothing from the outer rings.
- **Orchestration (inside the hexagon)** — the Hub's contexts and `Grimoire.AgentRuntime` —
  the code that sequences work and declares the ports it needs.
- **Adapters (edge)** — every `*.Adapters.*` namespace, plus repositories and artifact
  stores. Infrastructure packages live here and nowhere else: `Microsoft.Data.Sqlite` only
  in `OperationalState`, the Anthropic SDK only in `Core.Adapters.Anthropic`, outbound HTTP
  only in `Adapters.HttpFetch`, and, inside the Hub, process spawning only in the two
  adapters that need it (`Adapters.AgentProcess`, `Adapters.MarkItDown`).

### Bounded contexts

Harness side — `backend/src/Grimoire.Hub`:

| Context | Namespace | Owns | Port → adapter |
| --- | --- | --- | --- |
| Ingest Submission | `IngestSubmission`, `Conversion`, `ContentRoot` | Accepting a URL or file, converting it to markdown, storing the source artifacts | `IMarkdownConverter` → `Adapters.MarkItDown`, `IUrlContentFetcher` → `Adapters.HttpFetch` |
| Ingest Dispatch | `IngestDispatch` | Run queue, liveness supervision, reactivation and manual restart | — |
| Query | `QuerySubmission`, `QueryDispatch`, `QueryConversations` | Questions against the wiki and their conversation history | — |
| Lint & Remediation | `LintDispatch`, `LintFindings`, `RemediationTasks` | Wiki health findings and the authorized actions that resolve them | — |
| Agent Dispatch | `AgentDispatch` | Spawning and supervising agent child processes | `IAgentProcessLauncher` → `Adapters.AgentProcess` |
| Operational State | `OperationalState` | Durable run state, queue, status history (SQLite) | none — persistence is port-exempt |
| Realtime | `Realtime` | Pushing lifecycle changes to the board over SignalR | — |
| Task Artifact | `IngestTaskArtifact` | The per-task markdown record the UI reads | — |
| CLI | `Cli` | Operator commands against the same state the Hub uses | — |
| Runtime Paths | `Runtime.Paths` | Resolving the four independent directory roots — data, agent, wiki, memory ([ADR-022](docs/adr/ADR-022-minimal-directory-configuration-surface.md), [ADR-024](docs/adr/ADR-024-memory-directory-root.md)) | — |

Agent side — one process per agent, all on a shared runtime:

| Context | Namespace | Owns |
| --- | --- | --- |
| Agent Runtime | `Grimoire.AgentRuntime.Core` | The agent loop and the model boundary — `IModelClient` → `Core.Adapters.Anthropic` (and `Core.Adapters.Replay` for recorded runs) |
| Host & Composition | `Grimoire.AgentRuntime.Host`, `.Composition` | The shared hosting and DI wiring every agent process builds on ([ADR-013](docs/adr/ADR-013-unified-agent-platform-packaging-and-naming.md)) |
| Guardrails | `Grimoire.AgentRuntime.Guardrails` | The guarded tool layer: deny-by-default policy enforced at call time, plus shared-file write coordination |
| Instructions | `Grimoire.AgentRuntime.Instructions` | Loading and hashing the instruction files that govern agent behavior |
| Run Events / Telemetry / Wiki Log | `Grimoire.AgentRuntime.RunEvents`, `.Telemetry`, `.WikiLog` | What a run reports back to the Hub and to the wiki's own log |
| Ingest / Query / Lint Agent | `Grimoire.IngestAgent`, `.QueryAgent`, `.LintAgent` | Each agent's own entry point, CLI options, tool registry, instrumentation, and versioned instruction files |

Ports, adapter containment, and the guarded-write boundary are not conventions — they are
structural tests in [`backend/tests/Grimoire.ArchTests`](backend/tests/Grimoire.ArchTests),
each proven to detect violations by a Red/Green probe, and each run on every PR.

## Development process

This project is built with **Spec-Driven Development (Spec Kit)**: every feature goes
through `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement` →
`/speckit-converge`, gated by [`.specify/memory/constitution.md`](.specify/memory/constitution.md)
and the ADRs in [`docs/adr/`](docs/adr/) — [`docs/adr/index.md`](docs/adr/index.md) is the
single place to see which decisions currently govern the codebase. See
[CLAUDE.md](CLAUDE.md) for the full document map.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to set up your environment and the required
workflow for any change.

## Origin

Grimoire started from Andrej Karpathy's [llm-wiki](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f) gist — a pattern for building personal knowledge bases where an LLM incrementally builds and maintains a wiki instead of re-deriving answers from raw sources on every query. The original idea file is kept as source material at `docs/llm-wiki-nanoclaw-idea.md`.

Google Cloud later formalized the same pattern as the [Open Knowledge Format (OKF)](https://cloud.google.com/blog/products/data-analytics/how-the-open-knowledge-format-can-improve-data-sharing/?hl=en) — a vendor-neutral spec ([SPEC.md](https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md)) for representing knowledge as markdown files with YAML frontmatter, aimed at making LLM-maintained knowledge bases interoperable across tools.
