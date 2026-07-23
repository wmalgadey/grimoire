<script lang="ts">
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
</script>

<div class="flex flex-col gap-4" data-testid="query-conversation">
	{#each turns as turn (turn.turnId)}
		<article class="flex flex-col gap-2 rounded border border-slate-200 p-3" data-testid="query-turn">
			<p class="text-sm font-medium text-slate-900" data-testid="query-turn-prompt">{turn.prompt}</p>

			<div
				class="whitespace-pre-wrap text-sm text-slate-700"
				data-testid="query-turn-answer"
				data-turn-state={turn.state}
			>
				{turn.answer}
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
