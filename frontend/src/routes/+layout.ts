// ADR-027: server-side rendering is off for the whole app, which makes the SPA the
// frontend already was a declared one. No route has a server `load` — `board/+page.ts`
// redirects and `tasks/[taskId]/+page.ts` threads a route param through — and every
// screen loads its data in the browser over `fetch` and SignalR after mount, so SSR
// renders empty shells. Turning it off is what lets adapter-static serve the app from
// one fallback document behind the deployment's reverse proxy.
export const ssr = false;
