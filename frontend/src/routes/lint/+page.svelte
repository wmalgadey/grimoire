<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import DOMPurify from 'dompurify';
	import { marked } from 'marked';
	import { resolve } from '$app/paths';
	import {
		getLatestLintRun,
		getLintFindings,
		getLintRun,
		triggerLintRun
	} from '$lib/services/lintApi';
	import {
		presentRecordedFailure,
		toPresentedError,
		type PresentedError
	} from '$lib/services/apiError';
	import ApiErrorAlert from '$lib/components/ApiErrorAlert.svelte';
	import type { LintRun } from '$lib/types';

	// data-model.md "Lint Run": at most one run ever active, no per-run task board — the
	// page just shows the current/most recent run's status and its Findings Report.
	// Unlike Query, Lint has no streaming channel at all: the client polls for status.
	let run: LintRun | null = $state(null);
	let findingsContent: string | null = $state(null);
	let triggerError: PresentedError | null = $state(null);
	let pollHandle: ReturnType<typeof setInterval> | undefined;

	const statusLabels: Record<LintRun['status'], string> = {
		running: 'Running…',
		completed: 'Completed',
		failed: 'Failed'
	};

	// The Findings Report is agent-authored markdown, same untrusted-content rendering
	// discipline as QueryConversation.svelte's renderAnswer.
	function renderFindings(content: string): string {
		return DOMPurify.sanitize(marked.parse(content, { async: false }) as string);
	}

	async function refreshFindingsIfAvailable(runId: string, hasFindingsReport: boolean) {
		if (!hasFindingsReport) return;
		try {
			const report = await getLintFindings(runId);
			findingsContent = report.content;
		} catch {
			// Best-effort — the run's status is still shown even if the report fetch fails.
		}
	}

	function stopPolling() {
		if (pollHandle) {
			clearInterval(pollHandle);
			pollHandle = undefined;
		}
	}

	async function pollOnce() {
		if (!run || run.status !== 'running') {
			stopPolling();
			return;
		}

		try {
			const updated = await getLintRun(run.runId);
			run = updated;
			if (updated.status !== 'running') {
				stopPolling();
				await refreshFindingsIfAvailable(updated.runId, updated.hasFindingsReport);
			}
		} catch {
			// Transient fetch failure — the next poll tick tries again.
		}
	}

	function startPolling() {
		stopPolling();
		pollHandle = setInterval(() => void pollOnce(), 1000);
	}

	async function handleTrigger() {
		triggerError = null;
		try {
			const accepted = await triggerLintRun();
			run = {
				runId: accepted.runId,
				status: accepted.status,
				triggeredAt: accepted.triggeredAt,
				completedAt: null,
				failureReason: null,
				hasFindingsReport: false
			};
			findingsContent = null;
			startPolling();
		} catch (error) {
			triggerError = toPresentedError(error);
		}
	}

	onMount(() => {
		void (async () => {
			try {
				const latest = await getLatestLintRun();
				if (latest) {
					run = latest;
					if (latest.status === 'running') {
						startPolling();
					} else {
						await refreshFindingsIfAvailable(latest.runId, latest.hasFindingsReport);
					}
				}
			} catch {
				// No prior run to recover — the trigger button starts a fresh one.
			}
		})();
	});

	onDestroy(() => {
		stopPolling();
	});
</script>

<svelte:head>
	<title>Lint — Grimoire</title>
</svelte:head>

<main class="mx-auto flex min-h-screen max-w-3xl flex-col gap-6 bg-white p-6">
	<header class="sticky top-0 z-10 flex flex-col gap-1 bg-white/95 py-2 backdrop-blur">
		<div class="flex items-center justify-between gap-2">
			<h1 class="text-lg font-semibold text-slate-900">Wiki health check</h1>
			<div class="flex items-center gap-3">
				<a
					href={resolve('/')}
					class="text-sm font-medium text-slate-600 underline-offset-2 hover:underline"
					data-testid="nav-link-ingest">Submit a source</a
				>
				<a
					href={resolve('/query')}
					class="text-sm font-medium text-slate-600 underline-offset-2 hover:underline"
					data-testid="nav-link-query">Ask the wiki</a
				>
			</div>
		</div>
		<p class="text-sm text-slate-500">
			Run Lint to have it read the whole wiki, judge its health, and produce a Findings Report —
			contradictions, gaps, missing metadata, and orphan pages, each with a proposed remediation.
		</p>
	</header>

	<button
		type="button"
		class="self-start rounded bg-slate-900 px-4 py-2 text-sm font-medium text-white disabled:cursor-not-allowed disabled:bg-slate-300"
		disabled={run?.status === 'running'}
		onclick={handleTrigger}
		data-testid="lint-trigger-button"
	>
		{run?.status === 'running' ? 'Lint running…' : 'Run Lint'}
	</button>

	{#if triggerError}
		<ApiErrorAlert
			error={triggerError}
			testId="lint-trigger-error"
			onRetry={handleTrigger}
			onDismiss={() => (triggerError = null)}
		/>
	{/if}

	{#if run}
		<section
			class="flex flex-col gap-2 rounded border border-slate-200 p-3"
			data-testid="lint-run-status"
		>
			<div class="flex items-center gap-2">
				<span
					class="text-sm font-medium"
					class:text-blue-600={run.status === 'running'}
					class:text-emerald-600={run.status === 'completed'}
					class:text-red-600={run.status === 'failed'}
					data-testid="lint-run-state">{statusLabels[run.status]}</span
				>
				<span class="text-xs text-slate-400">run {run.runId}</span>
			</div>

			{#if run.status === 'failed' && run.failureReason}
				<!-- 024 FR-012: the recorded reason is unchanged; only its presentation is. A
				     provider rejection's "Model API error 400 (...)" framing moves into the
				     technical detail so the provider's own sentence is what reads first. -->
				<ApiErrorAlert
					error={presentRecordedFailure(run.failureReason)}
					testId="lint-run-failure-reason"
				/>
			{/if}
		</section>
	{/if}

	{#if findingsContent}
		<section
			class="lint-findings-body rounded border border-slate-200 p-4 text-sm text-slate-700"
			data-testid="lint-findings-report"
		>
			<!-- eslint-disable-next-line svelte/no-at-html-tags -->
			{@html renderFindings(findingsContent)}
		</section>
	{/if}
</main>

<style>
	/* Same rationale as QueryConversation.svelte's .query-turn-answer-body block: an
	   {@html}-injected Findings Report needs :global() to reach past Tailwind Preflight's
	   margin/list-style reset. */
	.lint-findings-body :global(> :first-child) {
		margin-top: 0;
	}
	.lint-findings-body :global(> :last-child) {
		margin-bottom: 0;
	}
	.lint-findings-body :global(p),
	.lint-findings-body :global(ul),
	.lint-findings-body :global(ol),
	.lint-findings-body :global(blockquote),
	.lint-findings-body :global(pre) {
		margin-top: 0;
		margin-bottom: 0.75rem;
	}
	.lint-findings-body :global(h1),
	.lint-findings-body :global(h2),
	.lint-findings-body :global(h3),
	.lint-findings-body :global(h4),
	.lint-findings-body :global(h5),
	.lint-findings-body :global(h6) {
		margin-top: 1rem;
		margin-bottom: 0.5rem;
		font-weight: 600;
	}
	.lint-findings-body :global(ul) {
		list-style: disc;
		padding-left: 1.5rem;
	}
	.lint-findings-body :global(ol) {
		list-style: decimal;
		padding-left: 1.5rem;
	}
	.lint-findings-body :global(li) {
		margin-bottom: 0.25rem;
	}
	.lint-findings-body :global(blockquote) {
		padding-left: 0.75rem;
		border-left: 2px solid var(--color-slate-300);
	}
	.lint-findings-body :global(pre) {
		overflow-x: auto;
		border-radius: 0.375rem;
		background-color: var(--color-slate-100);
		padding: 0.5rem 0.75rem;
	}
	.lint-findings-body :global(code) {
		font-family: ui-monospace, monospace;
		font-size: 0.85em;
	}
</style>
