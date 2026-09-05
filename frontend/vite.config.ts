import { existsSync, realpathSync } from 'node:fs';
import path from 'node:path';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';
import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';

// Some sandboxed dev/CI environments pre-install a Chromium build under
// PLAYWRIGHT_BROWSERS_PATH that Playwright cannot reach the registry to fetch itself.
// Use it when present; otherwise fall back to Playwright's normal browser resolution.
const preinstalledChromium = process.env.PLAYWRIGHT_BROWSERS_PATH
	? `${process.env.PLAYWRIGHT_BROWSERS_PATH}/chromium`
	: undefined;
const chromiumExecutablePath =
	preinstalledChromium && existsSync(preinstalledChromium) ? preinstalledChromium : undefined;

// The Hub (backend/src/Grimoire.Hub) listens on its own port (5255 by the http launch profile);
// the frontend dev/preview server proxies /api and /hubs to it so relative-path fetch() calls and
// the SignalR client (frontend/src/lib/services/*) reach the Hub without a separate reverse proxy.
const hubOrigin = process.env.VITE_HUB_ORIGIN ?? 'http://localhost:5255';
const hubProxy = {
	'/api': {
		target: hubOrigin,
		changeOrigin: true
	},
	'/hubs': {
		target: hubOrigin,
		changeOrigin: true,
		ws: true
	}
};

// #194: Stryker mutation-tests a *symlinked* copy of node_modules from inside a sandbox
// directory it creates under this one (the real files stay put; only the sandbox is
// temporary). Vite's dev server — which is what actually serves every module to the
// `client` project's real Chromium over HTTP — denies a request once it resolves (through
// the symlink) to a real path outside its configured root, and does so as a plain 404 with
// no warning: the `server` project never notices, because a Node worker imports modules
// directly and never goes through this HTTP layer at all. `fs.allow` normally covers only
// the workspace root and its ancestors, which does not include a sibling sandbox
// directory — so it needs the *real*, symlink-resolved location added explicitly. Outside
// a Stryker sandbox this resolves to the same directory `fs.allow` already defaults to, so
// it changes nothing for `vite dev`/`vite preview`/a plain `vitest run`.
//
// Guarded rather than assumed present: every other import above already requires
// node_modules to exist (Node resolves them from it before this line ever runs), so a
// missing directory here is unreachable in practice — but falling back to cwd rather than
// letting realpathSync throw costs nothing and keeps this config loadable standalone.
const nodeModulesPath = path.resolve('node_modules');
const realNodeModulesDir = existsSync(nodeModulesPath)
	? path.dirname(realpathSync(nodeModulesPath))
	: process.cwd();

export default defineConfig({
	plugins: [
		tailwindcss(),
		sveltekit({
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},

			// The app is a declared SPA. Every route's `load` is a redirect or a
			// route-param pass-through and every screen fetches its data in the browser
			// (fetch + SignalR) after mount, so there is nothing for a server to render.
			// `fallback` makes the proxy serve one document for every path and lets the
			// client router resolve it; `ssr = false` in src/routes/+layout.ts is the other
			// half. Reintroducing a server `load` means switching to adapter-node — see
			// deploy/README.md "Why it is shaped this way", not a change made here.
			adapter: adapter({ fallback: 'index.html' })
		})
	],
	server: {
		proxy: hubProxy,
		fs: { allow: [process.cwd(), realNodeModulesDir] }
	},
	// Vite's `server.proxy` does not carry over to `vite preview` (npm run build && npm run
	// preview) — it needs its own, otherwise that workflow silently drops all Hub/SignalR traffic.
	preview: {
		proxy: hubProxy
	},
	test: {
		expect: { requireAssertions: true },
		projects: [
			{
				extends: './vite.config.ts',
				test: {
					name: 'client',
					setupFiles: ['vitest-browser-svelte'],
					browser: {
						enabled: true,
						provider: playwright(
							chromiumExecutablePath
								? { launchOptions: { executablePath: chromiumExecutablePath } }
								: {}
						),
						instances: [{ browser: 'chromium', headless: true }]
					},
					include: ['src/**/*.svelte.{test,spec}.{js,ts}'],
					exclude: ['src/lib/server/**']
				}
			},

			{
				extends: './vite.config.ts',
				test: {
					name: 'server',
					environment: 'node',
					include: ['src/**/*.{test,spec}.{js,ts}'],
					exclude: ['src/**/*.svelte.{test,spec}.{js,ts}']
				}
			}
		]
	}
});
