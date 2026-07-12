<script lang="ts">
	import type { BoardTask, RunActivity } from '$lib/types';
	import StatusBadge from './StatusBadge.svelte';

	interface Props {
		task: BoardTask;
		runActivity?: RunActivity | null;
	}

	let { task, runActivity = null }: Props = $props();
</script>

<article
	class="flex flex-col gap-2 rounded-lg border border-slate-200 bg-white p-3 shadow-sm"
	data-testid="task-card"
	data-task-id={task.taskId}
>
	<div class="flex items-start justify-between gap-2">
		<h3 class="truncate text-sm font-medium text-slate-900" title={task.title}>
			{task.title}
		</h3>
		<StatusBadge status={task.status} />
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

	<div class="flex items-center justify-between text-xs text-slate-500">
		<time datetime={task.updatedAt}>{new Date(task.updatedAt).toLocaleString()}</time>
		<!-- taskLink points at the Hub JSON API (contracts/ingest-submission-api.md), not a SvelteKit route -->
		<!-- eslint-disable-next-line svelte/no-navigation-without-resolve -->
		<a href={task.taskLink} class="underline hover:no-underline" data-testid="task-card-link"
			>Details</a
		>
	</div>
</article>
