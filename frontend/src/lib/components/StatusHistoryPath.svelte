<script lang="ts">
	import type { HistoryStatus, LifecycleStage, StatusHistoryEntry } from '$lib/types';

	interface Props {
		/** Server-ordered history (contracts/http-api.md); may be empty for pre-feature tasks. */
		entries: StatusHistoryEntry[];
		/** Current status, used to synthesize a single entry when no history was recorded. */
		currentStatus: LifecycleStage;
	}

	let { entries, currentStatus }: Props = $props();

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

	// 023 FR-006: a task that predates the status history still gets a readable path —
	// its current status as a single entry — rather than an empty panel.
	const shown = $derived(
		entries.length > 0
			? entries
			: [{ status: currentStatus as HistoryStatus, enteredAt: '', detail: null }]
	);

	const isFallback = $derived(entries.length === 0);

	function formatTimestamp(value: string): string {
		if (!value) return '';
		const parsed = new Date(value);
		return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
	}
</script>

<section class="flex flex-col gap-2" data-testid="status-history-path">
	<h2 class="text-sm font-semibold text-slate-700">Status history</h2>
	<ol class="flex flex-col gap-1">
		{#each shown as entry, index (index)}
			<li
				class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 rounded-md px-2 py-1 text-sm {index ===
				shown.length - 1
					? 'bg-slate-100 font-medium text-slate-900'
					: 'text-slate-600'}"
				data-testid="status-history-entry"
				data-status={entry.status}
				data-current={index === shown.length - 1 ? 'true' : 'false'}
			>
				<span data-testid="status-history-entry-status">{labels[entry.status] ?? entry.status}</span>
				{#if entry.enteredAt}
					<time class="text-xs text-slate-500" datetime={entry.enteredAt}
						>{formatTimestamp(entry.enteredAt)}</time
					>
				{/if}
				{#if entry.detail}
					<span class="text-xs text-slate-500" data-testid="status-history-entry-detail"
						>{entry.detail}</span
					>
				{/if}
			</li>
		{/each}
	</ol>
	{#if isFallback}
		<p class="text-xs text-slate-400" data-testid="status-history-fallback-note">
			No recorded history for this task; showing its current status.
		</p>
	{/if}
</section>
