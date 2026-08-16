<script lang="ts">
	import { resolve } from '$app/paths';
	import { restartTask, IngestSubmissionApiError } from '$lib/services/ingestSubmissionsApi';
	import type { BoardTask, RunActivity } from '$lib/types';

	interface Props {
		task: BoardTask;
		runActivity?: RunActivity | null;
		/**
		 * 023 T049: the board's post-restart refresh. The card never mutates `task` itself —
		 * status is a read-only projection of harness-owned state (contracts/signalr-events.md),
		 * so after any restart attempt the board re-reads what the Hub actually says.
		 */
		onRefreshRequested?: () => void;
	}

	let { task, runActivity = null, onRefreshRequested }: Props = $props();

	// 023 T049 (US5/AC1, FR-010..FR-012): restart lives on the failed card as well as on the
	// detail page — the operator meets the failure on the board. Deliberately a button and not
	// a drag from `failed` to `queued`: the columns are a read-only projection of
	// harness-owned status, and ADR-025 §2 permits exactly one operator-driven transition
	// (`failed` → `queued` under the same task id), so a drag gesture would imply arbitrary
	// column moves the harness rejects. Shape follows RemediationTaskCard's card actions.
	let restarting = $state(false);
	let restartError: string | null = $state(null);

	async function handleRestart() {
		restarting = true;
		restartError = null;
		try {
			await restartTask(task.taskId);
		} catch (err) {
			restartError =
				err instanceof IngestSubmissionApiError
					? err.message
					: 'The restart request failed unexpectedly. Please try again.';
		} finally {
			restarting = false;
			onRefreshRequested?.();
		}
	}
</script>

<article
	class="flex flex-col gap-2 rounded-lg border border-slate-200 bg-white p-3 shadow-sm"
	data-testid="task-card"
	data-task-id={task.taskId}
>
	<!-- 023 FR-003/FR-004: the human-readable label is the card's primary text; the raw task
	     id stays visible underneath, muted, for the cases where the exact id is needed. -->
	<div class="flex flex-col gap-0.5">
		<h3
			class="truncate text-sm font-medium text-slate-900"
			title={task.title}
			data-testid="task-card-title"
		>
			{task.title}
		</h3>
		<p class="truncate text-xs text-slate-400" data-testid="task-card-task-id">{task.taskId}</p>
	</div>

	{#if task.status === 'queued' && task.queuePosition != null}
		<p class="text-xs text-slate-500" data-testid="task-card-queue-position">
			Position {task.queuePosition} in queue
		</p>
	{/if}

	{#if task.status === 'running' && runActivity}
		<p class="text-xs text-slate-500" data-testid="task-card-run-activity">
			{runActivity.modelTurns} model turns · {runActivity.toolCalls} tool calls · {runActivity.currentAction}
		</p>
	{/if}

	{#if task.status === 'failed' && task.failureReason}
		<p class="text-sm text-stage-failed" data-testid="task-card-failure-reason">
			{task.failureReason}
		</p>
	{/if}

	{#if task.status === 'failed'}
		<!-- FR-011: the control exists only for a finally-failed task; every other stage
		     renders nothing here. -->
		<div class="flex items-center gap-2">
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50"
				disabled={restarting}
				onclick={handleRestart}
				data-testid="task-card-restart-button"
			>
				{restarting ? 'Restarting…' : 'Restart'}
			</button>
		</div>
		{#if restartError}
			<!-- FR-012: a rejection (lost race, task already moved on) is shown, never silent. -->
			<p class="text-xs text-stage-failed" data-testid="task-card-restart-error">
				{restartError}
			</p>
		{/if}
	{/if}

	<div class="flex items-center justify-between text-xs text-slate-500">
		<time datetime={task.updatedAt}>{new Date(task.updatedAt).toLocaleString()}</time>
		<!-- Rendered detail view (006 research.md Decision 7): built from taskId, not taskLink —
		     taskLink keeps pointing at the Hub JSON API for machine consumers. -->
		<a
			href={resolve('/tasks/[taskId]', { taskId: task.taskId })}
			class="underline hover:no-underline"
			data-testid="task-card-link">Details</a
		>
	</div>
</article>
