<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import ConnectionStatusIndicator from '$lib/components/ConnectionStatusIndicator.svelte';
	import TaskRecordView from '$lib/components/TaskRecordView.svelte';
	import { createIngestLifecycleClient } from '$lib/services/ingestLifecycleClient';
	import { getTaskRecord } from '$lib/services/ingestSubmissionsApi';
	import type { ConnectionState, TaskRecord } from '$lib/types';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	let record: TaskRecord | null = $state(null);
	let loaded = $state(false);
	// 006 FR-010/SC-005: reuse the board's connection-state projection so the detail view
	// surfaces staleness while disconnected and resynchronizes on reconnect.
	let connectionState: ConnectionState = $state('connecting');

	let client: ReturnType<typeof createIngestLifecycleClient> | undefined;
	const unsubscribers: Array<() => void> = [];

	async function refresh() {
		const result = await getTaskRecord(data.taskId);
		record = result.status === 'ok' ? result.record : null;
		loaded = true;
	}

	onMount(() => {
		void refresh();

		client = createIngestLifecycleClient();
		unsubscribers.push(
			client.onTaskRecordChanged((event) => {
				// Only refetch for this route's own task (contracts/task-record-changed-event.md).
				if (event.taskId === data.taskId) {
					void refresh();
				}
			}),
			client.onReconnected(() => {
				// Resynchronize unconditionally after any connection gap (FR-010).
				void refresh();
			}),
			client.onConnectionStateChanged((state) => {
				connectionState = state;
			})
		);
		void client.start();
	});

	onDestroy(() => {
		for (const unsubscribe of unsubscribers) unsubscribe();
		void client?.stop();
	});
</script>

<svelte:head>
	<title>Task {data.taskId} — Grimoire</title>
</svelte:head>

<main class="mx-auto flex min-h-screen max-w-3xl flex-col gap-4 bg-white p-6">
	<header class="flex items-center justify-between gap-2">
		<h1 class="truncate text-lg font-semibold text-slate-900" data-testid="task-record-page-title">
			Task {data.taskId}
		</h1>
		<div class="flex shrink-0 items-center gap-3">
			<ConnectionStatusIndicator state={connectionState} />
			<a href={resolve('/')} class="text-sm text-slate-500 underline hover:no-underline"
				>Back to board</a
			>
		</div>
	</header>

	{#if loaded}
		<TaskRecordView {record} />
	{/if}
</main>
