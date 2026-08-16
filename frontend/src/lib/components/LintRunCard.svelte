<script lang="ts">
	import { presentRecordedFailure } from '$lib/services/apiError';
	import ApiErrorAlert from './ApiErrorAlert.svelte';
	import { resolve } from '$app/paths';
	import type { LintRun, LintRunStatus } from '$lib/types';

	// 015-lint-board-parity T014 (FR-001/FR-005/FR-006): the board's lint run card —
	// deliberately styled unlike ingest's TaskCard (violet accent + kind label) so the
	// activity kind is readable at a glance. `run = null` renders the "no lint activity
	// yet" state (US1 scenario 1).
	interface Props {
		run: LintRun | null;
	}

	let { run }: Props = $props();

	const statusLabels: Record<LintRunStatus, string> = {
		running: 'Running',
		completed: 'Completed',
		failed: 'Failed'
	};

	const statusClasses: Record<LintRunStatus, string> = {
		running: 'bg-violet-100 text-violet-700',
		completed: 'bg-emerald-100 text-emerald-700',
		failed: 'bg-red-100 text-red-700'
	};
</script>

<article
	class="flex flex-col gap-2 rounded-lg border-l-4 border-violet-500 bg-violet-50 p-3 shadow-sm"
	data-testid="lint-run-card"
	data-run-id={run?.runId ?? null}
>
	<div class="flex items-start justify-between gap-2">
		<div class="flex flex-col">
			<span class="text-xs font-semibold tracking-wide text-violet-700 uppercase">
				Wiki health check
			</span>
			{#if run}
				<h3 class="truncate text-sm font-medium text-slate-900" title={run.runId}>
					{run.runId}
				</h3>
			{/if}
		</div>
		{#if run}
			<span
				class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium {statusClasses[
					run.status
				]}"
				data-testid="lint-run-card-status"
				data-status={run.status}
			>
				{statusLabels[run.status]}
			</span>
		{/if}
	</div>

	{#if !run}
		<p class="text-sm text-slate-500" data-testid="lint-run-card-empty">
			No lint activity yet — trigger a run to check the wiki's health.
		</p>
	{:else}
		{#if run.status === 'failed' && run.failureReason}
			<ApiErrorAlert
				error={presentRecordedFailure(run.failureReason)}
				testId="lint-run-card-failure-reason"
			/>
		{/if}

		<div class="flex items-center justify-between text-xs text-slate-500">
			<time datetime={run.completedAt ?? run.triggeredAt}>
				{new Date(run.completedAt ?? run.triggeredAt).toLocaleString()}
			</time>
			<a
				href={resolve('/lint')}
				class="underline hover:no-underline"
				data-testid="lint-run-card-link">Findings</a
			>
		</div>
	{/if}
</article>
