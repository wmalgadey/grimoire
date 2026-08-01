<script lang="ts">
	import type { RemediationTaskBoardEntry, RemediationTaskState } from '$lib/types';

	// 015-lint-board-parity T026 (US3, FR-006): one board card per agent-proposed
	// remediation action — deliberately styled unlike ingest's TaskCard and the violet
	// lint card (amber accent + its own kind label) so the activity kind is readable at a
	// glance. Title is the verbatim agent-authored proposal title (Principle V), the
	// subtitle names the originating lint run, and each card is independently reviewable
	// (US3 scenario 3). The authorize/dismiss review actions are placeholders here —
	// they become live in US4 (T037).
	interface Props {
		task: RemediationTaskBoardEntry;
	}

	let { task }: Props = $props();

	const stateLabels: Record<RemediationTaskState, string> = {
		proposed: 'Proposed',
		authorized: 'Waiting',
		executing: 'Executing',
		completed: 'Completed',
		failed: 'Failed',
		not_applicable: 'Not applicable',
		dismissed: 'Dismissed'
	};

	const stateClasses: Record<RemediationTaskState, string> = {
		proposed: 'bg-amber-100 text-amber-800',
		authorized: 'bg-sky-100 text-sky-700',
		executing: 'bg-blue-100 text-blue-700',
		completed: 'bg-emerald-100 text-emerald-700',
		failed: 'bg-red-100 text-red-700',
		not_applicable: 'bg-slate-100 text-slate-600',
		dismissed: 'bg-slate-100 text-slate-600'
	};
</script>

<article
	class="flex flex-col gap-2 rounded-lg border-l-4 border-amber-500 bg-amber-50 p-3 shadow-sm"
	data-testid="remediation-task-card"
	data-task-id={task.taskId}
>
	<div class="flex items-start justify-between gap-2">
		<div class="flex min-w-0 flex-col">
			<span class="text-xs font-semibold tracking-wide text-amber-700 uppercase">
				Remediation proposal
			</span>
			<h3 class="text-sm font-medium text-slate-900" data-testid="remediation-task-card-title">
				{task.title}
			</h3>
			<p class="truncate text-xs text-slate-500" data-testid="remediation-task-card-run">
				From run {task.runId}
			</p>
		</div>
		<span
			class="inline-flex shrink-0 items-center rounded-full px-2.5 py-0.5 text-xs font-medium {stateClasses[
				task.state
			]}"
			data-testid="remediation-task-card-state"
			data-state={task.state}
		>
			{stateLabels[task.state]}
			{#if task.state === 'authorized' && task.queuePosition != null}
				<span class="ml-1" data-testid="remediation-task-card-queue-position"
					>#{task.queuePosition}</span
				>
			{/if}
		</span>
	</div>

	{#if (task.state === 'failed' || task.state === 'not_applicable') && task.outcomeReason}
		<!-- FR-005/FR-018: the outcome reason is surfaced on the card itself. -->
		<p
			class="text-sm {task.state === 'failed' ? 'text-red-700' : 'text-slate-600'}"
			data-testid="remediation-task-card-outcome-reason"
		>
			{task.outcomeReason}
		</p>
	{/if}

	{#if task.state === 'proposed'}
		<!-- T026: review-action placeholders — authorize/dismiss become live in US4 (T037). -->
		<div class="flex items-center gap-2" data-testid="remediation-task-card-actions">
			<button
				type="button"
				class="rounded bg-amber-600 px-3 py-1 text-xs font-medium text-white opacity-50"
				disabled
				title="Authorizing proposals arrives in a later increment."
				data-testid="remediation-task-card-authorize"
			>
				Authorize
			</button>
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-600 opacity-50"
				disabled
				title="Dismissing proposals arrives in a later increment."
				data-testid="remediation-task-card-dismiss"
			>
				Dismiss
			</button>
		</div>
	{/if}

	<div class="flex items-center justify-between text-xs text-slate-500">
		<time datetime={task.updatedAt}>
			{new Date(task.updatedAt).toLocaleString()}
		</time>
	</div>
</article>
