<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import KanbanColumn from '$lib/components/KanbanColumn.svelte';
	import { createBoardLifecycleStream } from '$lib/services/ingestLifecycleClient';
	import type { BoardTask, LifecycleStage } from '$lib/types';

	const stages: LifecycleStage[] = [
		'received',
		'converting',
		'queued',
		'running',
		'completed',
		'failed'
	];

	let tasks: BoardTask[] = $state([]);
	let stream: ReturnType<typeof createBoardLifecycleStream> | undefined;

	const tasksByStage = $derived(
		Object.fromEntries(
			stages.map((stage) => [stage, tasks.filter((t) => t.status === stage)])
		) as Record<LifecycleStage, BoardTask[]>
	);

	onMount(() => {
		stream = createBoardLifecycleStream((updated) => {
			tasks = updated;
		});
		void stream.start();
	});

	onDestroy(() => {
		void stream?.stop();
	});
</script>

<svelte:head>
	<title>Ingest board — Grimoire</title>
</svelte:head>

<main class="flex min-h-screen flex-col gap-4 bg-white p-6 dark:bg-slate-950">
	<header>
		<h1 class="text-lg font-semibold text-slate-900 dark:text-slate-100">Ingest board</h1>
		<p class="text-sm text-slate-500 dark:text-slate-400">
			Every submission, grouped by its current stage.
		</p>
	</header>

	<div class="flex gap-4 overflow-x-auto" data-testid="kanban-board">
		{#each stages as stage (stage)}
			<KanbanColumn {stage} tasks={tasksByStage[stage]} />
		{/each}
	</div>
</main>
