<script lang="ts">
	import type { BoardTask, LifecycleStage } from '$lib/types';
	import TaskCard from './TaskCard.svelte';

	interface Props {
		stage: LifecycleStage;
		tasks: BoardTask[];
	}

	let { stage, tasks }: Props = $props();

	const titles: Record<LifecycleStage, string> = {
		received: 'Received',
		converting: 'Converting',
		queued: 'Queued',
		running: 'Running',
		completed: 'Completed',
		failed: 'Failed'
	};
</script>

<section
	class="flex min-w-64 flex-1 flex-col gap-3 rounded-lg bg-slate-50 p-3"
	data-testid="kanban-column"
	data-stage={stage}
>
	<header class="flex items-center justify-between">
		<h2 class="text-sm font-semibold text-slate-700">{titles[stage]}</h2>
		<span class="text-xs text-slate-400" data-testid="kanban-column-count">{tasks.length}</span>
	</header>

	<div class="flex flex-col gap-2">
		{#each tasks as task (task.taskId)}
			<TaskCard {task} />
		{/each}
	</div>
</section>
