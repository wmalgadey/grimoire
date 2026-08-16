<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import AppNav from '$lib/components/AppNav.svelte';
	import ApiErrorAlert from '$lib/components/ApiErrorAlert.svelte';
	import BoardLane from '$lib/components/BoardLane.svelte';
	import CardPopover from '$lib/components/CardPopover.svelte';
	import FindingsDialog from '$lib/components/FindingsDialog.svelte';
	import { createBoardLifecycleStream } from '$lib/services/ingestLifecycleClient';
	import { getBoard, resumeQueue } from '$lib/services/ingestSubmissionsApi';
	import { createLintRunStream } from '$lib/services/lintLifecycleClient';
	import { createRemediationTaskStream } from '$lib/services/remediationLifecycleClient';
	import { toPresentedError, type PresentedError } from '$lib/services/apiError';
	import { conversations } from '$lib/stores/conversations.svelte';
	import {
		applyFilters,
		buildBoardItems,
		groupByLane,
		LANES,
		needsYou,
		needsYouSummary,
		type BoardFilters,
		type BoardItem
	} from '$lib/board';
	import type {
		BoardTask,
		ConnectionState,
		LifecycleStage,
		LintRun,
		RemediationTaskBoardEntry,
		RunActivity,
		RunActivityEvent
	} from '$lib/types';

	// The board from the Hi-Fi design (4a at rest, 4c for the popover contents and the empty
	// state). The live plumbing below is unchanged from the pre-design board — three
	// independent streams, one per activity kind (015 FR-015) — and everything the design added
	// sits on top of it as a projection: `$lib/board.ts` merges the three snapshots into lanes,
	// the filter row and the triage strip narrow that list, and a card opens a popover instead
	// of carrying its own actions.

	let tasks: BoardTask[] = $state([]);
	let stream: ReturnType<typeof createBoardLifecycleStream> | undefined;
	let lintRun: LintRun | null = $state(null);
	let lintStream: ReturnType<typeof createLintRunStream> | undefined;
	let remediationTasks: RemediationTaskBoardEntry[] = $state([]);
	let remediationStream: ReturnType<typeof createRemediationTaskStream> | undefined;
	// 004 FR-018: live loop-activity, keyed by taskId.
	let runActivityByTaskId: Record<string, RunActivity> = $state({});
	// 004 FR-021: queued tasks survive a Hub restart but wait for explicit resume.
	let queuePaused = $state(false);
	let resuming = $state(false);
	let resumeError: PresentedError | null = $state(null);
	// 004 FR-023: connection health, projected from the board's own SignalR lifecycle.
	let connectionState: ConnectionState = $state('connecting');
	// Distinguishes "the board is empty" from "the board has not answered yet", so the empty
	// state is never shown to someone who is simply still loading.
	let boardLoaded = $state(false);

	let filters: BoardFilters = $state({
		query: '',
		failedOnly: false,
		lastDayOnly: false,
		needsYouOnly: false
	});

	// Done and Failed start put away, as in the design's resting state; every lane can be
	// collapsed and reopened from its own icon.
	let collapsedLanes: Record<string, boolean> = $state({ completed: true, failed: true });

	let openCard: { item: BoardItem; position: { left: number; top: number } } | null = $state(null);
	let findingsRunId: string | null = $state(null);

	const items = $derived(
		buildBoardItems({ tasks, lintRun, remediationTasks, runActivityByTaskId })
	);
	const visibleItems = $derived(applyFilters(items, filters));
	const lanes = $derived(groupByLane(visibleItems));
	const needsYouCount = $derived(items.filter(needsYou).length);
	const boardEmpty = $derived(boardLoaded && items.length === 0);

	const laneEmptyText: Record<LifecycleStage, string> = {
		received: 'nothing new',
		converting: 'nothing converting',
		queued: 'queue empty',
		running: 'nothing running',
		completed: 'nothing completed',
		failed: 'nothing failed'
	};

	function toggleLane(stage: LifecycleStage) {
		collapsedLanes = { ...collapsedLanes, [stage]: !collapsedLanes[stage] };
		openCard = null;
	}

	/**
	 * The triage strip filters the board down to what is waiting on a person and opens the
	 * Failed lane so the failures it counted are actually visible; clicking again clears both
	 * ("It's now clickable — it filters the board down to just those tasks and expands the
	 * Failed lane; clicking again clears the filter", chat 3).
	 */
	function toggleNeedsYou() {
		const next = !filters.needsYouOnly;
		filters = { ...filters, needsYouOnly: next };
		collapsedLanes = next
			? { ...collapsedLanes, failed: false, completed: true }
			: { ...collapsedLanes, completed: true, failed: true };
		openCard = null;
	}

	function openPopover(item: BoardItem, anchor: HTMLElement) {
		const rect = anchor.getBoundingClientRect();
		// Keep the panel inside the viewport on both axes — the design went through two rounds
		// of exactly this ("Popover now clears the '+12 more' control and sits fully inside the
		// frame", chat 1).
		const left = Math.min(Math.max(12, rect.left), Math.max(12, window.innerWidth - 332));
		const top = Math.min(rect.bottom + 8, Math.max(12, window.innerHeight - 320));
		openCard = { item, position: { left, top } };
	}

	async function refreshQueueState() {
		try {
			const board = await getBoard();
			queuePaused = board.queuePaused ?? false;
		} catch {
			// 024 FR-011: a background refresh the user did not ask for stays silent rather than
			// displacing an error they are reading. The board still renders from the stream.
		}
	}

	// 023 T049 (FR-012): after a restart attempt the board — not the card — re-reads the Hub's
	// projection, so the operator ends up looking at the task's true current status.
	async function refreshBoard() {
		try {
			const board = await getBoard();
			tasks = board.tasks;
			queuePaused = board.queuePaused ?? false;
			boardLoaded = true;
		} catch {
			// Non-critical, same reasoning as refreshQueueState.
		}
	}

	// 024 SC-005: resuming is a user action, so its failure belongs in the shared presentation.
	async function handleResume() {
		resuming = true;
		resumeError = null;
		try {
			await resumeQueue();
			queuePaused = false;
		} catch (err) {
			resumeError = toPresentedError(err);
		} finally {
			resuming = false;
		}
	}

	function startConversation() {
		conversations.create();
		void goto(resolve('/query'));
	}

	onMount(() => {
		stream = createBoardLifecycleStream(
			(updated) => {
				tasks = updated;
				boardLoaded = true;
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
				},
				onConnectionStateChanged: (state: ConnectionState) => {
					connectionState = state;
				}
			}
		);
		void stream.start();
		void refreshQueueState();

		lintStream = createLintRunStream((run) => {
			lintRun = run;
		});
		// Non-critical: the ingest board still renders if the lint bootstrap/stream fails.
		void lintStream.start().catch(() => {});

		remediationStream = createRemediationTaskStream((entries) => {
			remediationTasks = entries;
		});
		void remediationStream.start().catch(() => {});
	});

	onDestroy(() => {
		void stream?.stop();
		void lintStream?.stop();
		void remediationStream?.stop();
	});
</script>

<svelte:head>
	<title>Board — Grimoire</title>
</svelte:head>

<div class="flex min-h-screen flex-col bg-white">
	<AppNav
		current="board"
		{connectionState}
		onNewConversation={startConversation}
		onIngestAccepted={() => void refreshBoard()}
	/>

	{#if boardEmpty}
		<div
			class="flex flex-1 flex-col items-center justify-center gap-3 px-6 py-20 text-center"
			data-testid="board-empty-state"
		>
			<div class="h-20 w-20 rounded-full bg-slate-100"></div>
			<h2 class="text-lg font-semibold text-slate-900">Nothing in flight.</h2>
			<p class="max-w-sm text-sm text-slate-500">
				Submit a URL or a file with <span class="font-medium">+ Ingest</span> and it appears here as Received
				within a second.
			</p>
		</div>
	{:else}
		<div class="flex flex-wrap items-center gap-2 px-6 py-3">
			<label
				class="flex min-w-[220px] flex-1 items-center gap-2 rounded-full border border-slate-300 px-3 py-1.5 sm:max-w-md"
			>
				<svg
					width="14"
					height="14"
					viewBox="0 0 24 24"
					fill="none"
					stroke="currentColor"
					stroke-width="2.5"
					stroke-linecap="round"
					class="shrink-0 text-slate-400"
					aria-hidden="true"
				>
					<circle cx="11" cy="11" r="7"></circle>
					<path d="m20 20-3.6-3.6"></path>
				</svg>
				<span class="sr-only">Search tasks</span>
				<input
					type="search"
					class="w-full border-0 bg-transparent text-sm text-slate-900 outline-none placeholder:text-slate-400"
					placeholder="search title or id"
					bind:value={filters.query}
					data-testid="board-search-input"
				/>
			</label>

			<div class="flex flex-wrap gap-2">
				<button
					type="button"
					class="rounded-full border px-3 py-1 text-xs {!filters.failedOnly && !filters.lastDayOnly
						? 'border-slate-900 text-slate-900'
						: 'border-slate-300 text-slate-500 hover:border-slate-400'}"
					aria-pressed={!filters.failedOnly && !filters.lastDayOnly}
					onclick={() => (filters = { ...filters, failedOnly: false, lastDayOnly: false })}
					data-testid="board-filter-all">All kinds</button
				>
				<button
					type="button"
					class="rounded-full border px-3 py-1 text-xs {filters.failedOnly
						? 'border-slate-900 text-slate-900'
						: 'border-slate-300 text-slate-500 hover:border-slate-400'}"
					aria-pressed={filters.failedOnly}
					onclick={() => (filters = { ...filters, failedOnly: !filters.failedOnly })}
					data-testid="board-filter-failed">Failed only</button
				>
				<button
					type="button"
					class="rounded-full border px-3 py-1 text-xs {filters.lastDayOnly
						? 'border-slate-900 text-slate-900'
						: 'border-slate-300 text-slate-500 hover:border-slate-400'}"
					aria-pressed={filters.lastDayOnly}
					onclick={() => (filters = { ...filters, lastDayOnly: !filters.lastDayOnly })}
					data-testid="board-filter-last-day">Last 24h</button
				>
			</div>
		</div>

		{#if needsYouCount > 0 || queuePaused}
			<div class="flex flex-wrap items-center gap-3 px-6 pb-3">
				{#if needsYouCount > 0}
					<button
						type="button"
						class="flex items-center gap-3 rounded-full border px-3 py-1.5 text-left {filters.needsYouOnly
							? 'border-amber-500 bg-amber-50'
							: 'border-transparent hover:border-amber-300 hover:bg-amber-50'}"
						aria-pressed={filters.needsYouOnly}
						onclick={toggleNeedsYou}
						title="Show only what needs you"
						data-testid="board-needs-you"
					>
						<span
							class="inline-flex items-center rounded-full border border-amber-500 px-2.5 py-0.5 text-xs font-medium text-amber-700"
							data-testid="board-needs-you-count">{needsYouCount} need you</span
						>
						<span class="text-sm text-slate-600">{needsYouSummary(items, queuePaused)}</span>
						<span class="text-xs text-amber-700 underline underline-offset-2">
							{filters.needsYouOnly ? 'showing only these — clear' : 'show only these'}
						</span>
					</button>
				{/if}

				{#if queuePaused}
					<!-- 004 FR-021: the queue does not restart itself after a Hub restart. -->
					<button
						type="button"
						class="text-sm text-slate-500 underline underline-offset-2 hover:text-slate-900 disabled:opacity-50"
						onclick={handleResume}
						disabled={resuming}
						data-testid="queue-resume-button">{resuming ? 'Resuming…' : 'Resume the queue'}</button
					>
				{/if}
			</div>
		{/if}

		{#if resumeError}
			<div class="px-6 pb-3">
				<ApiErrorAlert
					error={resumeError}
					testId="queue-resume-error"
					onRetry={handleResume}
					onDismiss={() => (resumeError = null)}
				/>
			</div>
		{/if}

		<div class="flex flex-1 items-start gap-3 overflow-x-auto px-6 pb-8" data-testid="kanban-board">
			{#each LANES as stage (stage)}
				<BoardLane
					{stage}
					items={lanes[stage]}
					collapsed={!!collapsedLanes[stage]}
					onToggle={() => toggleLane(stage)}
					onOpenCard={openPopover}
					maxVisible={stage === 'queued' ? 5 : undefined}
					emptyText={laneEmptyText[stage]}
				/>
			{/each}
		</div>
	{/if}
</div>

{#if openCard}
	<CardPopover
		item={openCard.item}
		position={openCard.position}
		onClose={() => (openCard = null)}
		onRefreshRequested={() => void refreshBoard()}
		onShowFindings={(runId) => {
			findingsRunId = runId;
			openCard = null;
		}}
	/>
{/if}

{#if findingsRunId}
	<FindingsDialog runId={findingsRunId} onClose={() => (findingsRunId = null)} />
{/if}
