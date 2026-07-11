import { existsSync } from 'node:fs';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vitest/config';
import { playwright } from '@vitest/browser-playwright';
import adapter from '@sveltejs/adapter-auto';
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

export default defineConfig({
	plugins: [
		tailwindcss(),
		sveltekit({
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},

			// adapter-auto only supports some environments, see https://svelte.dev/docs/kit/adapter-auto for a list.
			// If your environment is not supported, or you settled on a specific environment, switch out the adapter.
			// See https://svelte.dev/docs/kit/adapters for more information about adapters.
			adapter: adapter()
		})
	],
	server: {
		proxy: hubProxy
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
