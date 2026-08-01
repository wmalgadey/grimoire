<script lang="ts">
	import type { RemediationTaskBoardEntry, RemediationTaskState } from '$lib/types';
	import {
		authorizeRemediationTask,
		dismissRemediationTask,
		withdrawRemediationTaskAuthorization,
		RemediationApiError
	} from '$lib/services/remediationApi';

	// 015-lint-board-parity T026 (US3, FR-006) / T037 (US4): one board card per
	// agent-proposed remediation action — deliberately styled unlike ingest's TaskCard
	// and the violet lint card (amber accent + its own kind label) so the activity kind
	// is readable at a glance. Title is the verbatim agent-authored proposal title
	// (Principle V), the subtitle names the originating lint run, and each card is
	// independently reviewable (US3 scenario 3). T037 wires the authorize/dismiss/
	// withdraw actions to the real endpoints (contracts/remediation-task-api.md); this
	// component never mutates `task` on success — the parent board's
	// `remediationTaskLifecycleChanged` live stream (or its unknown-task board refresh)
	// is the single source of truth for the resulting state (data-model.md CAS
	// discipline), so a successful call here only clears any prior error and lets the
	// live update land through the normal prop flow.
	interface Props {
		task: RemediationTaskBoardEntry;
	}

	let { task }: Props = $props();

	let busy = $state(false);
	let errorMessage: string | null = $state(null);

	const stateLabels: Record<RemediationTaskState, string> = {
		proposed: 'Proposed',
		authorized: 'Waiting',
		executing: 'Executing',
		completed: 'Completed',
		failed: 'Failed',
		not_applicable: 'Not applicable',
		dismissed: 'Dismissed'
	};

	const stateClasses: Record<RemediationTaskState, string> = {
		proposed: 'bg-amber-100 text-amber-800',
		authorized: 'bg-sky-100 text-sky-700',
		executing: 'bg-blue-100 text-blue-700',
		completed: 'bg-emerald-100 text-emerald-700',
		failed: 'bg-red-100 text-red-700',
		not_applicable: 'bg-slate-100 text-slate-600',
		dismissed: 'bg-slate-100 text-slate-600'
	};

	// T037: every review action follows the same shape — clear any prior error, run the
	// call, and on failure surface `RemediationApiError`'s human-readable message
	// (contract discipline: 409s from a lost CAS race are never silent, FR-016/SC-004
	// precedent). Success intentionally does nothing locally beyond clearing the error —
	// see the class doc comment above.
	async function runAction(action: () => Promise<unknown>) {
		busy = true;
		errorMessage = null;
		try {
			await action();
		} catch (err) {
			errorMessage =
				err instanceof RemediationApiError
					? err.message
					: 'The request failed unexpectedly. Please try again.';
		} finally {
			busy = false;
		}
	}

	const handleAuthorize = () => runAction(() => authorizeRemediationTask(task.taskId));
	const handleDismiss = () => runAction(() => dismissRemediationTask(task.taskId));
	const handleWithdraw = () => runAction(() => withdrawRemediationTaskAuthorization(task.taskId));
</script>

<article
	class="flex flex-col gap-2 rounded-lg border-l-4 border-amber-500 bg-amber-50 p-3 shadow-sm"
	data-testid="remediation-task-card"
	data-task-id={task.taskId}
>
	<div class="flex items-start justify-between gap-2">
		<div class="flex min-w-0 flex-col">
			<span class="text-xs font-semibold tracking-wide text-amber-700 uppercase">
				Remediation proposal
			</span>
			<h3 class="text-sm font-medium text-slate-900" data-testid="remediation-task-card-title">
				{task.title}
			</h3>
			<p class="truncate text-xs text-slate-500" data-testid="remediation-task-card-run">
				From run {task.runId}
			</p>
		</div>
		<span
			class="inline-flex shrink-0 items-center rounded-full px-2.5 py-0.5 text-xs font-medium {stateClasses[
				task.state
			]}"
			data-testid="remediation-task-card-state"
			data-state={task.state}
		>
			{stateLabels[task.state]}
			{#if task.state === 'authorized' && task.queuePosition != null}
				<span class="ml-1" data-testid="remediation-task-card-queue-position"
					>#{task.queuePosition}</span
				>
			{/if}
		</span>
	</div>

	{#if (task.state === 'failed' || task.state === 'not_applicable') && task.outcomeReason}
		<!-- FR-005/FR-018: the outcome reason is surfaced on the card itself. -->
		<p
			class="text-sm {task.state === 'failed' ? 'text-red-700' : 'text-slate-600'}"
			data-testid="remediation-task-card-outcome-reason"
		>
			{task.outcomeReason}
		</p>
	{/if}

	{#if task.state === 'proposed'}
		<!-- T037: live authorize/dismiss actions (FR-009/FR-010). -->
		<div class="flex items-center gap-2" data-testid="remediation-task-card-actions">
			<button
				type="button"
				class="rounded bg-amber-600 px-3 py-1 text-xs font-medium text-white disabled:opacity-50"
				disabled={busy}
				onclick={handleAuthorize}
				data-testid="remediation-task-card-authorize"
			>
				Authorize
			</button>
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-600 disabled:opacity-50"
				disabled={busy}
				onclick={handleDismiss}
				data-testid="remediation-task-card-dismiss"
			>
				Dismiss
			</button>
		</div>
	{:else if task.state === 'authorized'}
		<!-- T037: withdraw authorization while still waiting (FR-016) — unavailable once
		     execution starts, since the card's own state will have moved to `executing`. -->
		<div class="flex items-center gap-2" data-testid="remediation-task-card-actions">
			<button
				type="button"
				class="rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-600 disabled:opacity-50"
				disabled={busy}
				onclick={handleWithdraw}
				data-testid="remediation-task-card-withdraw"
			>
				Withdraw authorization
			</button>
		</div>
	{/if}

	{#if errorMessage}
		<!-- FR-016/SC-004 discipline: a lost CAS race or any other rejection is shown, never silent. -->
		<p class="text-sm text-red-700" data-testid="remediation-task-card-error">
			{errorMessage}
		</p>
	{/if}

	<div class="flex items-center justify-between text-xs text-slate-500">
		<time datetime={task.updatedAt}>
			{new Date(task.updatedAt).toLocaleString()}
		</time>
	</div>
</article>
