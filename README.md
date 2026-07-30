# Grimoire

Grimoire is an LLM harness for a **Compound LLM-Wiki**: a personal knowledge base maintained
by LLM agents rather than by hand. Agents (Ingest, Query, and more to come) read raw sources,
maintain a structured markdown wiki, and answer questions over it — while a deterministic
backend harness owns dispatch, credential scoping, guardrails, and observability around them.

## Stack

- **Backend** (`backend/`) — .NET / C#, hexagonal (ports & adapters) architecture
- **Frontend** (`frontend/`) — SvelteKit
- **Agents** (`data/agents/`) — per-agent instruction files (`system-prompt.md`, `policy.json`) that govern each agent's behavior at runtime

## Development process

This project is built with **Spec-Driven Development (Spec Kit)**: every feature goes
through `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement` →
`/speckit-converge`, gated by [`.specify/memory/constitution.md`](.specify/memory/constitution.md)
and the ADRs in [`docs/adr/`](docs/adr/). See [CLAUDE.md](CLAUDE.md) for the full document map.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to set up your environment and the required
workflow for any change.

## Origin

Grimoire started from Andrej Karpathy's [llm-wiki](https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f) gist — a pattern for building personal knowledge bases where an LLM incrementally builds and maintains a wiki instead of re-deriving answers from raw sources on every query. The original idea file is kept as source material at `docs/llm-wiki-nanoclaw-idea.md`.

Google Cloud later formalized the same pattern as the [Open Knowledge Format (OKF)](https://cloud.google.com/blog/products/data-analytics/how-the-open-knowledge-format-can-improve-data-sharing/?hl=en) — a vendor-neutral spec ([SPEC.md](https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md)) for representing knowledge as markdown files with YAML frontmatter, aimed at making LLM-maintained knowledge bases interoperable across tools.
