# Grimoire Frontend — Ingest Intake Web UI

SvelteKit application implementing `specs/003-ingest-intake-webui/`: submit a source (URL,
Markdown, PDF, Office document) to the Hub and track its lifecycle on a Kanban board, per
ADR-001 (TypeScript + SvelteKit frontend stack).

## Stack

- SvelteKit + TypeScript
- Tailwind CSS for styling (`research.md` Decision 5)
- Vitest (`vitest-browser-svelte`, Playwright provider) for component tests, plus a Node-environment
  project for non-component unit tests
- ESLint + Prettier

## Developing

Requires Node ≥20.12 on `PATH` (see `.nvmrc`) even though Bun is the package manager —
some CLI tools (`svelte-kit`) are invoked via a `#!/usr/bin/env node` shebang, which
resolves through the system Node, not Bun's own runtime. Run `nvm use` (or equivalent)
before installing if your default Node is older.

```sh
bun install
bun run dev -- --open
```

## Testing

```sh
bun run test        # vitest run (client + server projects)
bun run check        # svelte-check
bun run lint          # prettier --check + eslint
```

## Building

```sh
bun run build
bun run preview
```

> `@sveltejs/adapter-auto` cannot detect a deployment target in this dev environment; swap in a
> concrete adapter (see [SvelteKit adapters](https://svelte.dev/docs/kit/adapters)) once a hosting
> target is chosen.
