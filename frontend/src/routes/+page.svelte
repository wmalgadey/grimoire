<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import KanbanColumn from '$lib/components/KanbanColumn.svelte';
	import SubmissionForm from '$lib/components/SubmissionForm.svelte';
	import { createBoardLifecycleStream } from '$lib/services/ingestLifecycleClient';
	import { getBoard, resumeQueue } from '$lib/services/ingestSubmissionsApi';
	import type { BoardTask, LifecycleStage, RunActivity, RunActivityEvent } from '$lib/types';

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
	// 004 FR-018: live loop-activity, keyed by taskId, layered onto the board without a
	// separate detail page.
	let runActivityByTaskId: Record<string, RunActivity> = $state({});
	// 004 FR-021: queued tasks survive a Hub restart but wait for explicit resume.
	let queuePaused = $state(false);
	let resuming = $state(false);

	const tasksByStage = $derived(
		Object.fromEntries(
			stages.map((stage) => [stage, tasks.filter((t) => t.status === stage)])
		) as Record<LifecycleStage, BoardTask[]>
	);

	async function refreshQueueState() {
		try {
			const board = await getBoard();
			queuePaused = board.queuePaused ?? false;
		} catch {
			// Non-critical: the board still renders from the lifecycle stream even if this
			// supplementary call fails.
		}
	}

	async function handleResume() {
		resuming = true;
		try {
			await resumeQueue();
			queuePaused = false;
		} catch {
			// Leave the banner visible; the user can retry.
		} finally {
			resuming = false;
		}
	}

	onMount(() => {
		stream = createBoardLifecycleStream(
			(updated) => {
				tasks = updated;
			},
			{
				onRunActivityChanged: (event: RunActivityEvent) => {
					runActivityByTaskId = {
						...runActivityByTaskId,
						[event.taskId]: {
							modelTurns: event.modelTurns,
							toolCalls: event.toolCalls,
							toolCallsByName: event.toolCallsByName,
							currentAction: event.currentAction
						}
					};
				}
			}
		);
		void stream.start();
		void refreshQueueState();
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

	{#if queuePaused}
		<div
			class="flex items-center justify-between rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800"
			data-testid="queue-paused-banner"
		>
			<span
				>Queue processing is paused after a restart. Queued tasks will not start automatically.</span
			>
			<button
				type="button"
				class="rounded bg-amber-600 px-3 py-1 text-xs font-medium text-white disabled:opacity-50"
				onclick={handleResume}
				disabled={resuming}
				data-testid="queue-resume-button"
			>
				{resuming ? 'Resuming…' : 'Resume queue'}
			</button>
		</div>
	{/if}

	<div class="flex gap-4 overflow-x-auto" data-testid="kanban-board">
		{#each stages as stage (stage)}
			<KanbanColumn {stage} tasks={tasksByStage[stage]} {runActivityByTaskId} />
		{/each}
	</div>
</main>
