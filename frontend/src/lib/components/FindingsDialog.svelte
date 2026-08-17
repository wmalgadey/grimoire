<script lang="ts">
	import ApiErrorAlert from './ApiErrorAlert.svelte';
	import { getLintFindings } from '$lib/services/lintApi';
	import { toPresentedError, type PresentedError } from '$lib/services/apiError';
	import { renderMarkdown } from '$lib/markdown';

	// The Findings Report, read from the board instead of from a route of its own. The /lint
	// page is gone by design ("Lint-Detail fällt weg: keine eigene Route … Lint-Karten werden
	// in task-board angezeigt", chat 3) — but the report the agent wrote is still the point of
	// running the check, so the lint card's popover opens it here.
	//
	// Agent-authored markdown drawn from untrusted source content, so it goes through the same
	// marked → DOMPurify path as every other rendered record (Principle V).
	interface Props {
		runId: string;
		onClose: () => void;
	}

	let { runId, onClose }: Props = $props();

	let content: string | null = $state(null);
	let loadError: PresentedError | null = $state(null);
	let loading = $state(true);

	async function load() {
		loading = true;
		loadError = null;
		try {
			content = (await getLintFindings(runId)).content;
		} catch (err) {
			loadError = toPresentedError(err);
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		void runId;
		void load();
	});
</script>

<svelte:window
	onkeydown={(event) => {
		if (event.key === 'Escape') onClose();
	}}
/>

<div
	class="fixed inset-0 z-50 grid place-items-center bg-slate-900/40 p-4"
	onclick={onClose}
	onkeydown={() => {}}
	role="presentation"
	data-testid="findings-dialog-backdrop"
>
	<div
		class="flex max-h-[85vh] w-full max-w-2xl flex-col gap-3 overflow-y-auto rounded-lg bg-white p-6 shadow-xl"
		role="dialog"
		tabindex="-1"
		aria-modal="true"
		aria-labelledby="findings-dialog-title"
		data-testid="findings-dialog"
		onclick={(event) => event.stopPropagation()}
		onkeydown={() => {}}
	>
		<div class="flex items-start justify-between gap-2">
			<div class="flex flex-col">
				<h2 id="findings-dialog-title" class="text-lg font-semibold text-slate-900">
					Findings report
				</h2>
				<span class="font-mono text-xs text-slate-400">{runId}</span>
			</div>
			<button
				type="button"
				class="rounded border border-slate-300 px-2 py-0.5 text-sm text-slate-500 hover:bg-slate-50"
				onclick={onClose}
				aria-label="Close"
				data-testid="findings-dialog-close">✕</button
			>
		</div>

		{#if loading}
			<p class="text-sm text-slate-500" data-testid="findings-dialog-loading">Loading…</p>
		{:else if loadError}
			<ApiErrorAlert error={loadError} testId="findings-dialog-error" onRetry={load} />
		{:else if content}
			<div
				class="findings-body text-sm leading-relaxed text-slate-700"
				data-testid="findings-dialog-body"
			>
				<!-- eslint-disable-next-line svelte/no-at-html-tags -->
				{@html renderMarkdown(content)}
			</div>
		{/if}
	</div>
</div>

<style>
	/* Same rationale as QueryConversation.svelte's answer body: {@html}-injected markdown gets
	   no scoping hash, and Tailwind Preflight has zeroed margins and list styles project-wide. */
	.findings-body :global(> :first-child) {
		margin-top: 0;
	}
	.findings-body :global(> :last-child) {
		margin-bottom: 0;
	}
	.findings-body :global(p),
	.findings-body :global(ul),
	.findings-body :global(ol),
	.findings-body :global(blockquote),
	.findings-body :global(pre) {
		margin-top: 0;
		margin-bottom: 0.75rem;
	}
	.findings-body :global(h1),
	.findings-body :global(h2),
	.findings-body :global(h3),
	.findings-body :global(h4),
	.findings-body :global(h5),
	.findings-body :global(h6) {
		margin-top: 1rem;
		margin-bottom: 0.5rem;
		font-weight: 600;
	}
	.findings-body :global(ul) {
		list-style: disc;
		padding-left: 1.5rem;
	}
	.findings-body :global(ol) {
		list-style: decimal;
		padding-left: 1.5rem;
	}
	.findings-body :global(li) {
		margin-bottom: 0.25rem;
	}
	.findings-body :global(blockquote) {
		padding-left: 0.75rem;
		border-left: 2px solid var(--color-slate-300);
	}
	.findings-body :global(pre) {
		overflow-x: auto;
		border-radius: 0.375rem;
		background-color: var(--color-slate-100);
		padding: 0.5rem 0.75rem;
	}
	.findings-body :global(code) {
		font-family: ui-monospace, monospace;
		font-size: 0.85em;
	}
</style>
