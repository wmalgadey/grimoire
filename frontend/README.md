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

```sh
npm install
npm run dev -- --open
```

## Testing

```sh
npm run test        # vitest run (client + server projects)
npm run check        # svelte-check
npm run lint          # prettier --check + eslint
```

## Building

```sh
npm run build
npm run preview
```

> `@sveltejs/adapter-auto` cannot detect a deployment target in this dev environment; swap in a
> concrete adapter (see [SvelteKit adapters](https://svelte.dev/docs/kit/adapters)) once a hosting
> target is chosen.
