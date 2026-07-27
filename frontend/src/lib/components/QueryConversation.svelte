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

			<div
				class="query-turn-answer-body text-sm text-slate-700"
				class:query-turn-answer-streaming={turn.state === 'running'}
				data-testid="query-turn-answer"
				data-turn-state={turn.state}
			>
				<!-- eslint-disable-next-line svelte/no-at-html-tags -->
				{@html renderAnswer(turn.answer)}{#if turn.state === 'running'}<span
						class="ml-0.5 animate-pulse font-bold text-blue-500"
						data-testid="query-turn-streaming-cursor"
						aria-hidden="true">▌</span
					>{/if}
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
