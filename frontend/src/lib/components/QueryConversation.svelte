<script lang="ts">
	import DOMPurify from 'dompurify';
	import { marked } from 'marked';
	import type { QueryTurn } from '$lib/types';

	interface Props {
		turns: QueryTurn[];
		onInterrupt?: (turnId: string) => void;
	}

	let { turns, onInterrupt }: Props = $props();

	const stateLabels: Record<QueryTurn['state'], string> = {
		running: 'Answering…',
		completed: 'Completed',
		interrupted: 'Interrupted',
		failed: 'Failed'
	};

	// The answer is agent-authored markdown (bold, lists, [[wikilink]] citations per the
	// Query system prompt's citation convention) — render it formatted, not as raw text.
	// Sanitized because the source content it draws from is untrusted (Principle V),
	// same reasoning as TaskRecordView.svelte's rendering of ingest task records.
	function renderAnswer(answer: string): string {
		return DOMPurify.sanitize(marked.parse(answer, { async: false }) as string);
	}
</script>

<div class="flex flex-col gap-4" data-testid="query-conversation">
	{#each turns as turn (turn.turnId)}
		<article
			class="flex flex-col gap-2 rounded border p-3"
			class:border-slate-200={turn.state !== 'running'}
			class:border-blue-300={turn.state === 'running'}
			class:bg-blue-50={turn.state === 'running'}
			data-testid="query-turn"
		>
			<p class="text-sm font-medium text-slate-900" data-testid="query-turn-prompt">
				{turn.prompt}
			</p>

			<div data-testid="query-turn-answer" data-turn-state={turn.state}>
				{#if turn.state === 'running'}
					<!-- Streamed text is partial/unclosed markdown (e.g. a list or bold span mid-word),
					     so it is shown as plain text rather than parsed — parsing incomplete markdown
					     produces broken HTML. `white-space: pre-wrap` keeps the model's own line breaks
					     visible, and the muted, smaller styling signals "still forming", distinct from
					     the fully-formatted answer shown once the turn is terminal. -->
					<p class="query-turn-answer-streaming-text text-xs whitespace-pre-wrap text-slate-400">
						{turn.answer}<span
							class="ml-0.5 animate-pulse font-bold text-blue-500"
							data-testid="query-turn-streaming-cursor"
							aria-hidden="true">▌</span
						>
					</p>
				{:else}
					<div class="query-turn-answer-body text-sm text-slate-700">
						<!-- eslint-disable-next-line svelte/no-at-html-tags -->
						{@html renderAnswer(turn.answer)}
					</div>
				{/if}
			</div>

			<div class="flex items-center gap-2">
				<span
					class="text-xs"
					class:text-slate-400={turn.state === 'running'}
					class:text-emerald-600={turn.state === 'completed'}
					class:text-amber-600={turn.state === 'interrupted'}
					class:text-red-600={turn.state === 'failed'}
					data-testid="query-turn-state"
				>
					{stateLabels[turn.state]}
				</span>

				{#if turn.state === 'running' && onInterrupt}
					<button
						type="button"
						class="rounded border border-slate-300 px-2 py-0.5 text-xs text-slate-600"
						onclick={() => onInterrupt(turn.turnId)}
						data-testid="query-turn-stop-button"
					>
						Stop
					</button>
				{/if}
			</div>

			{#if turn.state === 'failed' && turn.failureReason}
				<p class="text-xs text-red-600" data-testid="query-turn-failure-reason">
					{turn.failureReason}
				</p>
			{/if}
		</article>
	{/each}
</div>

<style>
	/* {@html}-injected markdown isn't part of Svelte's compiled markup, so its elements
	   never receive Svelte's scoping hash — :global() is required to reach them at all.
	   Needed because Tailwind Preflight zeroes margin/padding/list-style on every block
	   element project-wide, which otherwise collapses paragraphs/lists/headings in an
	   agent answer into an unreadable, spacing-free run. */
	.query-turn-answer-body :global(> :first-child) {
		margin-top: 0;
	}
	.query-turn-answer-body :global(> :last-child) {
		margin-bottom: 0;
	}
	.query-turn-answer-body :global(p),
	.query-turn-answer-body :global(ul),
	.query-turn-answer-body :global(ol),
	.query-turn-answer-body :global(blockquote),
	.query-turn-answer-body :global(pre) {
		margin-top: 0;
		margin-bottom: 0.75rem;
	}
	.query-turn-answer-body :global(h1),
	.query-turn-answer-body :global(h2),
	.query-turn-answer-body :global(h3),
	.query-turn-answer-body :global(h4),
	.query-turn-answer-body :global(h5),
	.query-turn-answer-body :global(h6) {
		margin-top: 1rem;
		margin-bottom: 0.5rem;
		font-weight: 600;
	}
	.query-turn-answer-body :global(ul) {
		list-style: disc;
		padding-left: 1.5rem;
	}
	.query-turn-answer-body :global(ol) {
		list-style: decimal;
		padding-left: 1.5rem;
	}
	.query-turn-answer-body :global(li) {
		margin-bottom: 0.25rem;
	}
	.query-turn-answer-body :global(blockquote) {
		padding-left: 0.75rem;
		border-left: 2px solid var(--color-slate-300);
	}
	.query-turn-answer-body :global(pre) {
		overflow-x: auto;
		border-radius: 0.375rem;
		background-color: var(--color-slate-100);
		padding: 0.5rem 0.75rem;
	}
	.query-turn-answer-body :global(code) {
		font-family: ui-monospace, monospace;
		font-size: 0.85em;
	}
</style>
