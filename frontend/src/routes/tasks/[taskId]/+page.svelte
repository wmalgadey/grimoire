<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import ConnectionStatusIndicator from '$lib/components/ConnectionStatusIndicator.svelte';
	import TaskMessageThread from '$lib/components/TaskMessageThread.svelte';
	import TaskRecordView from '$lib/components/TaskRecordView.svelte';
	import { createIngestLifecycleClient } from '$lib/services/ingestLifecycleClient';
	import { getTaskRecord } from '$lib/services/ingestSubmissionsApi';
	import { fetchRemediationTaskMessages, getRemediationTask } from '$lib/services/remediationApi';
	import { createRemediationLifecycleClient } from '$lib/services/remediationLifecycleClient';
	import type {
		ConnectionState,
		RemediationTaskDetail,
		RemediationTaskMessage,
		TaskRecord
	} from '$lib/types';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	// 015-lint-board-parity T043: remediation task ids always contain "-remediation-"
	// (Grimoire.Hub.LintDispatch.LintRunCoordinator's task-id shape) — a cheap,
	// server-authoritative-shape-derived branch that keeps ingest task detail rendering
	// completely untouched below (FR-015-style discipline: no shared code path mutated).
	const isRemediationTask = $derived(data.taskId.includes('-remediation-'));

	// ── ingest task detail (unchanged) ──────────────────────────────────────────────
	let record: TaskRecord | null = $state(null);
	let loaded = $state(false);
	// 006 FR-010/SC-005: reuse the board's connection-state projection so the detail view
	// surfaces staleness while disconnected and resynchronizes on reconnect.
	let connectionState: ConnectionState = $state('connecting');

	let client: ReturnType<typeof createIngestLifecycleClient> | undefined;

	async function refresh() {
		const result = await getTaskRecord(data.taskId);
		record = result.status === 'ok' ? result.record : null;
		loaded = true;
	}

	// ── remediation task detail (T043, US5) ─────────────────────────────────────────
	let remediationTask: RemediationTaskDetail | null = $state(null);
	let remediationMessages: RemediationTaskMessage[] = $state([]);
	let remediationLoaded = $state(false);

	let remediationClient: ReturnType<typeof createRemediationLifecycleClient> | undefined;

	async function refreshRemediationTask() {
		remediationTask = await getRemediationTask(data.taskId);
		remediationLoaded = true;
	}

	async function refreshRemediationMessages() {
		remediationMessages = (await fetchRemediationTaskMessages(data.taskId)).messages;
	}

	const unsubscribers: Array<() => void> = [];

	onMount(() => {
		if (isRemediationTask) {
			void refreshRemediationTask();
			void refreshRemediationMessages();

			remediationClient = createRemediationLifecycleClient();
			unsubscribers.push(
				remediationClient.onRemediationTaskLifecycleChanged((event) => {
					if (event.taskId === data.taskId) {
						void refreshRemediationTask();
					}
				}),
				remediationClient.onRemediationMessageTurnChanged((event) => {
					if (event.taskId === data.taskId) {
						// Re-fetch both: the reply landed in the record (messages) and
						// messageTurnActive flips back to false (detail).
						void refreshRemediationMessages();
						void refreshRemediationTask();
					}
				}),
				remediationClient.onReconnected(() => {
					void refreshRemediationTask();
					void refreshRemediationMessages();
				}),
				remediationClient.onConnectionStateChanged((state) => {
					connectionState = state;
				})
			);
			void remediationClient.start();
			return;
		}

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
		void remediationClient?.stop();
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

	{#if isRemediationTask}
		{#if remediationLoaded && remediationTask}
			<section class="flex flex-col gap-1" data-testid="remediation-task-detail-header">
				<h2 class="text-sm font-medium text-slate-900">{remediationTask.title}</h2>
				<p class="text-xs text-slate-500">
					From run {remediationTask.runId} — state: {remediationTask.state}
				</p>
				{#if remediationTask.outcomeReason}
					<p class="text-xs text-slate-600">{remediationTask.outcomeReason}</p>
				{/if}
			</section>
			<TaskMessageThread
				taskId={data.taskId}
				taskState={remediationTask.state}
				attachedContext={remediationTask.attachedContext}
				messages={remediationMessages}
				messageTurnActive={remediationTask.messageTurnActive}
			/>
		{/if}
	{:else if loaded}
		<TaskRecordView {record} />
	{/if}
</main>
