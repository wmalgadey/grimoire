<script lang="ts">
	import { resolve } from '$app/paths';
	import type { BoardTask, RunActivity } from '$lib/types';

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
