<script lang="ts">
	import type { HistoryStatus, StatusHistoryEntry } from '$lib/types';

	interface Props {
		/** Server-ordered history (contracts/http-api.md). */
		entries: StatusHistoryEntry[];
	}

	let { entries }: Props = $props();

	const labels: Record<HistoryStatus, string> = {
		received: 'Received',
		converting: 'Converting',
		queued: 'Queued',
		running: 'Running',
		completed: 'Completed',
		failed: 'Failed',
		liveness_interrupted: 'Liveness interrupted',
		reactivated: 'Reactivated',
		restarted: 'Restarted'
	};

	function formatTimestamp(value: string): string {
		const parsed = new Date(value);
		return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
	}
</script>

<section class="flex flex-col gap-2" data-testid="status-history-path">
	<h2 class="text-sm font-semibold text-slate-700">Status history</h2>
	<ol class="flex flex-col gap-1">
		{#each entries as entry, index (index)}
			<li
				class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 rounded-md px-2 py-1 text-sm {index ===
				entries.length - 1
					? 'bg-slate-100 font-medium text-slate-900'
					: 'text-slate-600'}"
				data-testid="status-history-entry"
				data-status={entry.status}
				data-current={index === entries.length - 1 ? 'true' : 'false'}
			>
				<span data-testid="status-history-entry-status">{labels[entry.status] ?? entry.status}</span>
				<time class="text-xs text-slate-500" datetime={entry.enteredAt}
					>{formatTimestamp(entry.enteredAt)}</time
				>
				{#if entry.detail}
					<span class="text-xs text-slate-500" data-testid="status-history-entry-detail"
						>{entry.detail}</span
					>
				{/if}
			</li>
		{/each}
	</ol>
</section>
