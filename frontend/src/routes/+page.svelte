<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import KanbanColumn from '$lib/components/KanbanColumn.svelte';
	import SubmissionForm from '$lib/components/SubmissionForm.svelte';
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
	<title>Ingest — Grimoire</title>
</svelte:head>

<main class="mx-auto flex min-h-screen max-w-5xl flex-col gap-6 bg-white p-6">
	<header class="flex flex-col gap-1">
		<h1 class="text-lg font-semibold text-slate-900">Submit a source</h1>
		<p class="text-sm text-slate-500">
			Submit a URL, Markdown, PDF, or Office document to ingest into the wiki. Its progress appears
			on the board below as soon as it's accepted.
		</p>
	</header>

	<SubmissionForm />

	<div class="flex gap-4 overflow-x-auto" data-testid="kanban-board">
		{#each stages as stage (stage)}
			<KanbanColumn {stage} tasks={tasksByStage[stage]} />
		{/each}
	</div>
</main>
