<script lang="ts">
	import { resolve } from '$app/paths';
	import ConnectionStatusIndicator from './ConnectionStatusIndicator.svelte';
	import IngestDialog from './IngestDialog.svelte';
	import LintTriggerPopover from './LintTriggerPopover.svelte';
	import { obsidianVaultUri } from '$lib/wikiLinks';
	import { NEW_CONVERSATION_PARAM } from '$lib/stores/conversations.svelte';
	import { getServerVersion } from '$lib/services/serverVersionApi';
	import type { ConnectionState } from '$lib/types';

	// The app shell from the Hi-Fi design: brand, two destinations, and the actions on the
	// right ("+ ask should be moved to the right buttons" — chat 3, which is also where Run
	// Lint ends up once the lint route is gone). Every screen carries it, so an action is
	// never a page away.
	interface Props {
		current: 'board' | 'conversations';
		connectionState: ConnectionState;
		/** Starts a fresh conversation; the caller decides whether that also navigates. */
		onNewConversation?: () => void;
		/** Fired when an ingest submission is accepted, so the board can refresh. */
		onIngestAccepted?: () => void;
	}

	let { current, connectionState, onNewConversation, onIngestAccepted }: Props = $props();

	let ingestOpen = $state(false);

	// The version of the Hub the board is connected to, shown in the connection indicator's
	// hover panel. Fetched on every transition *into* `connected` rather than once on mount:
	// a tab left open across a redeploy reconnects to a different build, and re-reading it at
	// exactly that moment is what keeps the panel describing the server actually on the other
	// end. Cleared while there is no connection, because a version from the previous one is a
	// claim this component can no longer stand behind.
	let serverVersion = $state<string | null>(null);
	let previousConnectionState: ConnectionState | null = null;

	$effect(() => {
		const current = connectionState;
		const justConnected = current === 'connected' && previousConnectionState !== 'connected';
		previousConnectionState = current;

		if (justConnected) {
			void getServerVersion().then((version) => {
				serverVersion = version;
			});
		} else if (current === 'disconnected') {
			serverVersion = null;
		}
	});

	const tabClass = (active: boolean) =>
		'rounded-full px-4 py-1 text-sm font-medium ' +
		(active ? 'bg-slate-900 text-white' : 'text-slate-600 hover:bg-slate-100');
</script>

<header
	class="flex flex-wrap items-center gap-3 border-b border-slate-200 px-6 py-3"
	data-testid="app-nav"
>
	<span class="mr-2 text-lg font-semibold text-slate-900">Grimoire</span>

	<nav class="flex gap-1" aria-label="Main">
		<a
			href={resolve('/')}
			class={tabClass(current === 'board')}
			aria-current={current === 'board' ? 'page' : undefined}
			data-testid="nav-link-board">Board</a
		>
		<a
			href={resolve('/query')}
			class={tabClass(current === 'conversations')}
			aria-current={current === 'conversations' ? 'page' : undefined}
			data-testid="nav-link-query">Conversations</a
		>
	</nav>

	<div class="ml-auto flex items-center gap-2">
		<button
			type="button"
			class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700"
			onclick={() => (ingestOpen = true)}
			data-testid="nav-ingest-button">+ Ingest</button
		>

		{#if onNewConversation}
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
				onclick={onNewConversation}
				title="Start a new conversation with fresh context"
				data-testid="nav-ask-button">+ Ask</button
			>
		{:else}
			<!-- From another screen this is a link, so the intent has to survive the navigation:
			     the flag tells the conversations page to open a fresh thread rather than the
			     overview, which is what the label promises. The path still goes through
			     resolve(); the rule fires on the query string appended to it, which resolve()
			     has no parameter for. -->
			<!-- eslint-disable svelte/no-navigation-without-resolve -->
			<a
				href={`${resolve('/query')}?${NEW_CONVERSATION_PARAM}=1`}
				class="rounded border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
				title="Start a new conversation with fresh context"
				data-testid="nav-ask-button">+ Ask</a
			>
			<!-- eslint-enable svelte/no-navigation-without-resolve -->
		{/if}

		<LintTriggerPopover />

		<!-- Obsidian is the wiki reader; Grimoire never tries to be one (chat 1). An
		     `obsidian://` URI is not a SvelteKit route, so resolve() does not apply. -->
		<!-- eslint-disable svelte/no-navigation-without-resolve -->
		<a
			href={obsidianVaultUri()}
			class="inline-flex items-center gap-1.5 rounded border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
			title="Open the wiki in Obsidian"
			data-testid="nav-obsidian-link"
		>
			<svg
				width="14"
				height="14"
				viewBox="0 0 24 24"
				fill="none"
				stroke="currentColor"
				stroke-width="2.5"
				stroke-linecap="round"
				stroke-linejoin="round"
				aria-hidden="true"
			>
				<path d="M14 4h6v6"></path>
				<path d="M20 4 11 13"></path>
				<path d="M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4"></path>
			</svg>
			Open in Obsidian
		</a>
		<!-- eslint-enable svelte/no-navigation-without-resolve -->

		<ConnectionStatusIndicator state={connectionState} {serverVersion} />
	</div>
</header>

<IngestDialog
	open={ingestOpen}
	onClose={() => (ingestOpen = false)}
	onAccepted={() => {
		ingestOpen = false;
		onIngestAccepted?.();
	}}
/>
