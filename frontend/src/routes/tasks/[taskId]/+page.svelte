<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import AppNav from '$lib/components/AppNav.svelte';
	import StatusHistoryPath from '$lib/components/StatusHistoryPath.svelte';
	import TaskMessageThread from '$lib/components/TaskMessageThread.svelte';
	import TaskRecordView from '$lib/components/TaskRecordView.svelte';
	import { createIngestLifecycleClient } from '$lib/services/ingestLifecycleClient';
	import {
		getTaskDetail,
		getTaskRecord,
		IngestSubmissionApiError,
		restartTask
	} from '$lib/services/ingestSubmissionsApi';
	import { fetchRemediationTaskMessages, getRemediationTask } from '$lib/services/remediationApi';
	import { createRemediationLifecycleClient } from '$lib/services/remediationLifecycleClient';
	import { toPresentedError, type PresentedError } from '$lib/services/apiError';
	import ApiErrorAlert from '$lib/components/ApiErrorAlert.svelte';
	import type {
		ConnectionState,
		LifecycleStage,
		RemediationTaskDetail,
		RemediationTaskMessage,
		TaskDetail,
		TaskRecord
	} from '$lib/types';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	// Task detail as the design lays it out (5a): the history on the left, what the agent has
	// been doing on the right, and the record it wrote underneath. The data behind each region
	// is unchanged — the detail endpoint is still authoritative for history (023 FR-006) and
	// the record still arrives on its own channel.

	// 015-lint-board-parity T043: remediation task ids always contain "-remediation-".
	const isRemediationTask = $derived(data.taskId.includes('-remediation-'));

	let record: TaskRecord | null = $state(null);
	let loaded = $state(false);
	// 006 FR-010/SC-005: the same connection projection the board uses, so staleness is visible.
	let connectionState: ConnectionState = $state('connecting');
	let detail: TaskDetail | null = $state(null);

	let client: ReturnType<typeof createIngestLifecycleClient> | undefined;

	async function refresh() {
		const result = await getTaskRecord(data.taskId);
		record = result.status === 'ok' ? result.record : null;
		loaded = true;
	}

	async function refreshDetail() {
		try {
			detail = await getTaskDetail(data.taskId);
		} catch (err) {
			// 024 FR-011: a background refresh is deliberately not routed to the shared error
			// presentation — it must not displace the restart error the user is reading. Only the
			// Hub actually saying the task is gone clears it; a refresh we simply could not
			// perform leaves the last known detail standing.
			if (err instanceof IngestSubmissionApiError && err.status === 404) {
				detail = null;
			}
		}
	}

	// 023 T033 (US5, FR-010..FR-012): restart is shown only for a failed task, disabled while
	// in flight; any rejection re-reads the true current state instead of trusting the click.
	let restarting = $state(false);
	let restartError: PresentedError | null = $state(null);

	async function handleRestart() {
		restarting = true;
		restartError = null;
		try {
			await restartTask(data.taskId);
		} catch (err) {
			restartError = toPresentedError(err);
		} finally {
			restarting = false;
			await refreshDetail();
		}
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

	const statusLabels: Record<LifecycleStage, string> = {
		received: 'Received',
		converting: 'Converting',
		queued: 'Queued',
		running: 'Running',
		completed: 'Completed',
		failed: 'Failed'
	};

	// #129: run activity is in-memory in the coordinator and dropped at the terminal
	// transition, so `runActivity` is null for every completed or failed task — not only for
	// one no agent ever picked up. Saying "no agent has picked this up" to someone looking at
	// a task an agent just finished is simply false; it is only true before dispatch. The
	// counters themselves are not recoverable here (nothing persists them — that half of the
	// issue pairs with #135); what this can do is stop claiming the run never happened.
	const isTerminalStatus = (status: LifecycleStage | undefined) =>
		status === 'completed' || status === 'failed';

	const activityEmptyTextFor = (status: LifecycleStage | undefined) =>
		status === 'completed' || status === 'failed'
			? 'This run has finished. Turn and tool-call counts are only kept while an agent is running, so they are not available now — the task record below is what it produced.'
			: status === 'running'
				? 'The agent has started; turns and tool calls appear here as it reports them.'
				: 'No agent has picked this up yet, so there are no turns or tool calls to show.';

	const statusClasses: Record<LifecycleStage, string> = {
		received: 'bg-slate-100 text-slate-700',
		converting: 'bg-amber-100 text-amber-800',
		queued: 'bg-sky-100 text-sky-700',
		running: 'bg-blue-100 text-blue-700',
		completed: 'bg-emerald-100 text-emerald-700',
		failed: 'bg-red-100 text-red-700'
	};

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
		void refreshDetail();

		client = createIngestLifecycleClient();
		unsubscribers.push(
			client.onTaskRecordChanged((event) => {
				if (event.taskId === data.taskId) {
					void refresh();
				}
			}),
			client.onLifecycleChanged((event) => {
				// 023: any lifecycle event for this task — board stage or history-only status
				// alike — means the history moved on; re-read it.
				if (event.taskId === data.taskId) {
					void refreshDetail();
				}
			}),
			client.onReconnected(() => {
				// Resynchronize unconditionally after any connection gap (FR-010).
				void refresh();
				void refreshDetail();
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

<div class="flex min-h-screen flex-col bg-white">
	<AppNav current="board" {connectionState} />

	<header class="flex flex-wrap items-start gap-4 px-6 pt-5 pb-4">
		<!-- 023 FR-003/FR-004: the label heads the page, the raw id stays beneath it —
		     selectable as plain text, for when the exact identifier is what you need. -->
		<div class="flex min-w-0 flex-1 flex-col">
			<h1
				class="truncate text-lg font-semibold text-slate-900"
				data-testid="task-record-page-title"
			>
				{(isRemediationTask ? remediationTask?.title : detail?.title) ?? data.taskId}
			</h1>
			<p
				class="truncate font-mono text-xs text-slate-400 select-all"
				data-testid="task-detail-task-id"
			>
				{data.taskId}
			</p>
		</div>

		<div class="flex shrink-0 items-center gap-2">
			{#if detail?.status}
				<span
					class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium {statusClasses[
						detail.status
					]}"
					data-testid="task-detail-status"
					data-status={detail.status}>{statusLabels[detail.status]}</span
				>
			{/if}
			{#if detail?.status === 'failed'}
				<button
					type="button"
					class="rounded bg-slate-900 px-3 py-1.5 text-sm font-medium text-white disabled:cursor-not-allowed disabled:opacity-50"
					data-testid="task-restart-button"
					disabled={restarting}
					onclick={handleRestart}
				>
					{restarting ? 'Restarting…' : 'Restart'}
				</button>
			{/if}
			<a
				href={resolve('/')}
				class="rounded border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
				data-testid="task-back-to-board">← Board</a
			>
		</div>
	</header>

	{#if restartError}
		<div class="px-6 pb-3">
			<ApiErrorAlert
				error={restartError}
				testId="task-restart-error"
				onDismiss={() => (restartError = null)}
			/>
		</div>
	{/if}

	{#if isRemediationTask}
		{#if remediationLoaded && remediationTask}
			<div class="flex flex-col gap-4 border-t border-slate-200 px-6 py-5">
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
			</div>
		{/if}
	{:else if loaded}
		{#snippet runDetail()}
			<div class="flex flex-wrap items-stretch">
				<div class="flex min-w-72 flex-[1.15] flex-col gap-3 border-r border-slate-200 px-6 py-5">
					{#if detail && (detail.statusHistory?.length ?? 0) > 0}
						<StatusHistoryPath entries={detail.statusHistory ?? []} />
					{:else}
						<h2 class="text-xs font-semibold tracking-wider text-slate-600 uppercase">
							Status history
						</h2>
						<p class="max-w-[34ch] text-sm text-slate-500" data-testid="task-history-empty">
							Nothing has happened yet beyond the submission. Stages appear here as the task moves.
						</p>
					{/if}
				</div>

				<div class="flex min-w-72 flex-1 flex-col gap-3 px-6 py-5">
					<h2 class="text-xs font-semibold tracking-wider text-slate-600 uppercase">
						Agent activity
					</h2>
					{#if detail?.runActivity}
						<!-- 004 FR-018: loop mechanics only — turns, tool calls and the current action are
						     what the Hub publishes. The design also sketches the wiki pages the agent
						     wrote; no endpoint reports them today, so nothing stands in for them here.
						     TODO(backend): expose pages touched per task (the guarded write tool already
						     knows them) and this region gains the "Wrote / Writing" list from the design. -->
						<div class="flex gap-5 text-sm text-slate-700" data-testid="task-activity-counts">
							<span>{detail.runActivity.modelTurns} model turns</span>
							<span>{detail.runActivity.toolCalls} tool calls</span>
						</div>
						{#if detail.runActivity.currentAction}
							<p class="text-sm text-slate-500" data-testid="task-activity-current">
								{detail.runActivity.currentAction}
							</p>
						{/if}
						<div
							class="flex flex-col gap-1 rounded-lg bg-slate-50 p-3"
							data-testid="task-tool-calls"
						>
							{#each Object.entries(detail.runActivity.toolCallsByName) as [name, count] (name)}
								<span class="font-mono text-xs text-slate-600">{name} ×{count}</span>
							{/each}
						</div>
					{:else}
						<p class="max-w-[34ch] text-sm text-slate-500" data-testid="task-activity-empty">
							{activityEmptyTextFor(detail?.status)}
						</p>
					{/if}
				</div>
			</div>
		{/snippet}

		<!-- #129 (direction 1): both regions describe a run *in progress* — the activity
		     counters only exist while the agent process is alive, and the status path is
		     live-progress framing. On a finished task that is the whole upper half of the
		     page reporting on work that is already over, above the thing that actually
		     matters. Collapsed behind a disclosure once the task is terminal, so the task
		     record leads and the run detail is one line away for anyone who wants it. -->
		{#if isTerminalStatus(detail?.status)}
			<details class="border-t border-slate-200" data-testid="task-run-detail">
				<summary
					class="cursor-pointer px-6 py-3 text-xs font-semibold tracking-wider text-slate-600 uppercase hover:bg-slate-50"
					data-testid="task-run-detail-summary">Run detail</summary
				>
				{@render runDetail()}
			</details>
		{:else}
			<div class="border-t border-slate-200">
				{@render runDetail()}
			</div>
		{/if}

		<section class="flex flex-col gap-3 border-t border-slate-200 px-6 py-5">
			<h2 class="text-xs font-semibold tracking-wider text-slate-600 uppercase">Task record</h2>
			<TaskRecordView {record} source={detail?.source} />
		</section>
	{/if}
</div>
