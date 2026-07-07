<script lang="ts">
	import type { BoardTask } from '$lib/types';
	import StatusBadge from './StatusBadge.svelte';

	interface Props {
		task: BoardTask;
	}

	let { task }: Props = $props();
</script>

<article
	class="flex flex-col gap-2 rounded-lg border border-slate-200 bg-white p-3 shadow-sm dark:border-slate-700 dark:bg-slate-800"
	data-testid="task-card"
	data-task-id={task.taskId}
>
	<div class="flex items-start justify-between gap-2">
		<h3 class="truncate text-sm font-medium text-slate-900 dark:text-slate-100" title={task.title}>
			{task.title}
		</h3>
		<StatusBadge status={task.status} />
	</div>

	{#if task.status === 'failed' && task.failureReason}
		<p class="text-sm text-stage-failed" data-testid="task-card-failure-reason">
			{task.failureReason}
		</p>
	{/if}

	<div class="flex items-center justify-between text-xs text-slate-500 dark:text-slate-400">
		<time datetime={task.updatedAt}>{new Date(task.updatedAt).toLocaleString()}</time>
		<!-- taskLink points at the Hub JSON API (contracts/ingest-submission-api.md), not a SvelteKit route -->
		<!-- eslint-disable-next-line svelte/no-navigation-without-resolve -->
		<a href={task.taskLink} class="underline hover:no-underline" data-testid="task-card-link"
			>Details</a
		>
	</div>
</article>
