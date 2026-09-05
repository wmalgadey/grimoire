<script lang="ts">
	import { resolve } from '$app/paths';
	import ApiErrorAlert from './ApiErrorAlert.svelte';
	import {
		presentRecordedFailure,
		toPresentedError,
		type PresentedError
	} from '$lib/services/apiError';
	import { restartIngestTask } from '$lib/services/ingestSubmissionsApi';
	import {
		authorizeRemediationTask,
		dismissRemediationTask,
		withdrawRemediationTaskAuthorization
	} from '$lib/services/remediationApi';
	import type { BoardItem } from '$lib/board';

	// The quick view the board settled on: "the pop over is nice for a quick view. maybe
	// something like that could be added for the cards" (chat 1). It carries what the card had
	// no room for — the live action, the last tool calls, the full failure — and the actions the
	// card used to hold inline.
	//
	// Every action here is one the Hub already exposes, and none of them mutates the item: the
	// board's streams remain the single source of truth for what happens next (015 CAS
	// discipline), so a successful call closes the popover and lets the update land normally.
	// "Open details" stays the only route into the detail view (chat 3).
	interface Props {
		item: BoardItem;
		/** Viewport coordinates, computed by the board from the card that opened this. */
		position: { left: number; top: number };
		onClose: () => void;
		/** The board re-reads the Hub's projection after a restart attempt (023 T049). */
		onRefreshRequested?: () => void;
		onShowFindings?: (runId: string) => void;
	}

	let { item, position, onClose, onRefreshRequested, onShowFindings }: Props = $props();

	let busy = $state(false);
	let actionError: PresentedError | null = $state(null);

	async function run(action: () => Promise<unknown>, { refresh = false } = {}) {
		busy = true;
		actionError = null;
		try {
			await action();
			onClose();
		} catch (err) {
			actionError = toPresentedError(err);
		} finally {
			busy = false;
			if (refresh) onRefreshRequested?.();
		}
	}

	const toolCalls = $derived(
		item.runActivity ? Object.entries(item.runActivity.toolCallsByName) : []
	);

	/** The lines a stage shows when there is no live activity block to show instead. */
	const plainLines = $derived.by(() => {
		if (item.kind === 'remediation' && item.remediationState === 'proposed') {
			return [
				`Proposed by wiki health check ${item.sourceRunId}`,
				'Authorizing queues it as a normal task.'
			];
		}
		if (item.lane === 'queued' && item.queuePosition != null) {
			return [`Position ${item.queuePosition} in the queue`];
		}
		if (item.kind === 'lint') {
			return [
				item.lane === 'completed'
					? 'The health check read the wiki and filed its findings.'
					: item.lane === 'running'
						? 'Reading the wiki now. Findings arrive on the board as they are filed.'
						: 'The health check stopped before it finished.'
			];
		}
		return [];
	});
</script>

<svelte:window
	onkeydown={(event) => {
		if (event.key === 'Escape') onClose();
	}}
/>

<!-- Backdrop under the panel: clicking anywhere outside dismisses (chat 3). -->
<div
	class="fixed inset-0 z-40"
	onclick={onClose}
	onkeydown={() => {}}
	role="presentation"
	data-testid="card-popover-backdrop"
></div>

<div
	class="fixed z-50 flex w-80 flex-col gap-2 rounded-lg border border-slate-200 bg-white p-4 shadow-xl"
	style="left: {position.left}px; top: {position.top}px"
	role="dialog"
	aria-label={item.title}
	data-testid="card-popover"
	data-task-id={item.id}
>
	<div class="flex items-start gap-2">
		<span class="flex-1 text-sm font-medium text-slate-900" data-testid="card-popover-title"
			>{item.title}</span
		>
		<button
			type="button"
			class="rounded border border-slate-300 px-2 text-sm text-slate-500 hover:bg-slate-50"
			onclick={onClose}
			aria-label="Close"
			data-testid="card-popover-close">✕</button
		>
	</div>

	<span class="truncate font-mono text-xs text-slate-400">{item.id}</span>

	{#if item.failureReason}
		<ApiErrorAlert
			error={presentRecordedFailure(item.failureReason)}
			testId="card-popover-failure-reason"
		/>
	{/if}

	{#if item.runActivity}
		<!-- 004 FR-018: the live loop snapshot, which is the whole point of opening a running
		     card — what it is doing now, and what it has been calling. -->
		<div class="flex flex-col gap-1.5 rounded-md bg-blue-50 p-3" data-testid="card-popover-now">
			<span class="text-xs font-semibold tracking-wider text-blue-800 uppercase">
				Now · {item.runActivity.currentAction}
			</span>
			<span class="text-xs text-blue-900">
				{item.runActivity.modelTurns} model turns · {item.runActivity.toolCalls} tool calls
			</span>
			{#each toolCalls as [name, count] (name)}
				<span class="font-mono text-xs text-blue-900">{name} ×{count}</span>
			{/each}
		</div>
	{:else if plainLines.length > 0}
		<div class="flex flex-col gap-1">
			{#each plainLines as line (line)}
				<span class="text-xs text-slate-600">{line}</span>
			{/each}
		</div>
	{/if}

	{#if actionError}
		<ApiErrorAlert
			error={actionError}
			testId="card-popover-error"
			onDismiss={() => (actionError = null)}
		/>
	{/if}

	<div class="mt-1 flex flex-wrap gap-2">
		{#if item.kind === 'ingest' && item.lane === 'failed'}
			<!-- 023 FR-010..FR-012: restart is still met where the operator meets the failure; it
			     moved from the card's face into the card's quick view, nothing more. -->
			<button
				type="button"
				class="rounded bg-blue-600 px-3 py-1 text-xs font-medium text-white disabled:opacity-50"
				disabled={busy}
				onclick={() => run(() => restartIngestTask(item.id), { refresh: true })}
				data-testid="card-popover-restart">{busy ? 'Restarting…' : 'Restart'}</button
			>
		{/if}

		{#if item.remediationState === 'proposed'}
			<button
				type="button"
				class="rounded bg-amber-600 px-3 py-1 text-xs font-medium text-white disabled:opacity-50"
				disabled={busy}
				onclick={() => run(() => authorizeRemediationTask(item.id))}
				data-testid="card-popover-authorize">Authorize</button
			>
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-600 disabled:opacity-50"
				disabled={busy}
				onclick={() => run(() => dismissRemediationTask(item.id))}
				data-testid="card-popover-dismiss">Dismiss</button
			>
		{:else if item.remediationState === 'authorized'}
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-600 disabled:opacity-50"
				disabled={busy}
				onclick={() => run(() => withdrawRemediationTaskAuthorization(item.id))}
				data-testid="card-popover-withdraw">Withdraw authorization</button
			>
		{/if}

		{#if item.kind === 'lint' && item.lintRunId && item.lintHasFindingsReport && onShowFindings}
			<!-- With the /lint route gone, the Findings Report is read here rather than on a page
			     of its own — the report itself is still the agent's verbatim output. -->
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-600"
				onclick={() => onShowFindings(item.lintRunId!)}
				data-testid="card-popover-findings">Findings report</button
			>
		{/if}

		{#if item.detailTaskId}
			<a
				href={resolve('/tasks/[taskId]', { taskId: item.detailTaskId })}
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-700 hover:bg-slate-50"
				data-testid="card-popover-details">Open details</a
			>
		{/if}
	</div>
</div>
