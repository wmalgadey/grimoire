<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { SvelteMap, SvelteSet } from 'svelte/reactivity';
	import { resolve } from '$app/paths';
	import ConnectionStatusIndicator from '$lib/components/ConnectionStatusIndicator.svelte';
	import QueryConversation from '$lib/components/QueryConversation.svelte';
	import QueryPromptForm from '$lib/components/QueryPromptForm.svelte';
	import ApiErrorAlert from '$lib/components/ApiErrorAlert.svelte';
	import { toPresentedError, type PresentedError } from '$lib/services/apiError';
	import {
		applyAnswerChunk,
		applyTurnChanged,
		createQueryLifecycleClient,
		type QueryLifecycleClient
	} from '$lib/services/queryLifecycleClient';
	import {
		getQueryTurn,
		interruptQueryTurn,
		submitQueryTurn
	} from '$lib/services/querySubmissionApi';
	import type { ConnectionState, QueryTurn, QueryTurnStatus } from '$lib/types';

	function newConversationId(): string {
		return crypto.randomUUID();
	}

	// data-model.md Query Conversation: client-side, ephemeral, one per browser window.
	let conversationId = $state(newConversationId());
	let turns: QueryTurn[] = $state([]);
	let activeTurnId: string | null = $state(null);
	let connectionState: ConnectionState = $state('connecting');
	let submissionError: PresentedError | null = $state(null);
	// Kept so the retry affordance can re-run exactly what failed (FR-008).
	let lastSubmittedPrompt: string | null = $state(null);

	let client: QueryLifecycleClient | undefined;
	const seenTurnChangedKeys = new SvelteSet<string>();
	const lastAppliedSequenceByTurnId = new SvelteMap<string, number>();

	function updateTurn(turnId: string, update: (turn: QueryTurn) => QueryTurn) {
		turns = turns.map((t) => (t.turnId === turnId ? update(t) : t));
	}

	async function handleSubmit(prompt: string) {
		submissionError = null;
		lastSubmittedPrompt = prompt;

		// ADR-014 (011-query-conversations): the submission carries only the prompt —
		// the Hub sources follow-up context from its Conversation Record. The client-side
		// `turns` state stays for on-screen display only (UI/UX unchanged, FR-009).
		try {
			const accepted = await submitQueryTurn(conversationId, prompt);
			const turn: QueryTurn = {
				turnId: accepted.turnId,
				conversationId: accepted.conversationId,
				position: accepted.position,
				prompt,
				answer: '',
				state: accepted.state
			};
			turns = [...turns, turn];
			activeTurnId = accepted.turnId;
			lastAppliedSequenceByTurnId.set(accepted.turnId, 0);
		} catch (error) {
			submissionError = toPresentedError(error);
		}
	}

	function retrySubmit() {
		if (lastSubmittedPrompt !== null) void handleSubmit(lastSubmittedPrompt);
	}

	// 024 SC-005: stopping a turn is a user action, so its failure is a request failure and
	// belongs in the shared presentation — the same reasoning that moved `handleResume` out of
	// silence. This one used to be swallowed on the grounds that the turn's true state arrives
	// via `queryTurnChanged` regardless. That inverts precisely where it matters: an interrupt
	// that never reached the Hub produces no such event, so the answer keeps streaming and the
	// click is indistinguishable from a no-op.
	let interruptError: PresentedError | null = $state(null);
	// Kept so the retry affordance can re-run exactly what failed (FR-008), as retrySubmit does.
	let lastInterruptedTurnId: string | null = $state(null);

	async function handleInterrupt(turnId: string) {
		interruptError = null;
		lastInterruptedTurnId = turnId;
		try {
			await interruptQueryTurn(turnId);
		} catch (error) {
			interruptError = toPresentedError(error);
		}
	}

	function retryInterrupt() {
		if (lastInterruptedTurnId !== null) void handleInterrupt(lastInterruptedTurnId);
	}

	// On reconnect, refresh the active turn's authoritative state via REST before resuming
	// the stream (contracts/query-conversation-api.md ## Rules) — mirrors
	// ingestLifecycleClient.ts's createBoardLifecycleStream's onReconnected → refresh().
	async function refreshActiveTurn(turnId: string) {
		try {
			const authoritative = await getQueryTurn(turnId);
			updateTurn(turnId, (turn) => ({
				...turn,
				answer: authoritative.answer,
				state: authoritative.state,
				failureReason: authoritative.failureReason
			}));
			if (authoritative.state !== 'running') {
				activeTurnId = null;
			}
		} catch {
			// Best-effort reconciliation; subsequent lifecycle events still apply normally.
		}
	}

	function startNewConversation() {
		conversationId = newConversationId();
		turns = [];
		activeTurnId = null;
		lastAppliedSequenceByTurnId.clear();
		seenTurnChangedKeys.clear();
	}

	// spec.md Edge Cases / Assumptions: an in-flight turn at reload time is treated as
	// interrupted. `pagehide` fires reliably on reload/navigation/tab-close (unlike a
	// SignalR disconnect, which also fires on a transient network blip the automatic-
	// reconnect logic is meant to recover from — interrupting there would be wrong).
	// `keepalive: true` lets the request complete even as the page is unloading.
	function handlePageHide() {
		if (!activeTurnId) return;
		// 024 FR-011: silence here is deliberate, unlike handleInterrupt above. The page is
		// unloading, so there is no surface left to present a failure on — but the promise still
		// needs settling, or a failed unload-time interrupt is an unhandled rejection.
		void interruptQueryTurn(activeTurnId, (input, init) =>
			fetch(input, { ...init, keepalive: true })
		).catch(() => {});
	}

	onMount(() => {
		client = createQueryLifecycleClient();

		client.onAnswerChunk((event) => {
			const lastSequence = lastAppliedSequenceByTurnId.get(event.turnId) ?? 0;
			updateTurn(event.turnId, (turn) => {
				const { answer, lastAppliedSequence } = applyAnswerChunk(turn.answer, event, lastSequence);
				lastAppliedSequenceByTurnId.set(event.turnId, lastAppliedSequence);
				return { ...turn, answer };
			});
		});

		client.onTurnChanged((event) => {
			if (!applyTurnChanged(event, seenTurnChangedKeys)) return;

			updateTurn(event.turnId, (turn) => ({
				...turn,
				state: event.toState as QueryTurnStatus,
				failureReason: event.failureReason
			}));

			if (event.turnId === activeTurnId && event.toState !== 'running') {
				activeTurnId = null;
			}
		});

		client.onConnectionStateChanged((state) => {
			connectionState = state;
		});

		client.onReconnected(() => {
			if (activeTurnId) {
				void refreshActiveTurn(activeTurnId);
			}
		});

		window.addEventListener('pagehide', handlePageHide);

		void client.start();
	});

	onDestroy(() => {
		window.removeEventListener('pagehide', handlePageHide);
		void client?.stop();
	});
</script>

<svelte:head>
	<title>Query — Grimoire</title>
</svelte:head>

<main class="mx-auto flex min-h-screen max-w-3xl flex-col gap-6 bg-white p-6">
	<header class="sticky top-0 z-10 flex flex-col gap-1 bg-white/95 py-2 backdrop-blur">
		<div class="flex items-center justify-between gap-2">
			<h1 class="text-lg font-semibold text-slate-900">Ask the wiki</h1>
			<div class="flex items-center gap-2">
				<a
					href={resolve('/')}
					class="text-sm font-medium text-slate-600 underline-offset-2 hover:underline"
					data-testid="nav-link-ingest">Submit a source</a
				>
				<a
					href={resolve('/lint')}
					class="text-sm font-medium text-slate-600 underline-offset-2 hover:underline"
					data-testid="nav-link-lint">Wiki health check</a
				>
				<ConnectionStatusIndicator state={connectionState} />
				<button
					type="button"
					class="rounded border border-slate-300 px-2 py-1 text-xs text-slate-600"
					onclick={startNewConversation}
					data-testid="query-new-conversation-button"
				>
					New conversation
				</button>
			</div>
		</div>
		<p class="text-sm text-slate-500">
			Ask a question and watch the answer stream in, grounded in wiki content.
		</p>
		<p class="text-xs text-slate-400" data-testid="query-context-hint">
			Follow-up questions in this conversation see everything asked and answered so far. Starting a
			new conversation clears that context.
		</p>
	</header>

	<QueryConversation {turns} onInterrupt={handleInterrupt} />

	<!-- Its own region, beside the conversation whose Stop button raised it: the board page sets
	     the precedent that one page carries one error slot per action (queue resume, lint
	     trigger), which is what keeps a submission failure and an interrupt failure from
	     overwriting each other. -->
	{#if interruptError}
		<ApiErrorAlert
			error={interruptError}
			testId="query-interrupt-error"
			onRetry={retryInterrupt}
			onDismiss={() => (interruptError = null)}
		/>
	{/if}

	<QueryPromptForm disabled={activeTurnId !== null} onSubmit={handleSubmit} />

	{#if submissionError}
		<ApiErrorAlert
			error={submissionError}
			testId="query-submission-error"
			onRetry={retrySubmit}
			onDismiss={() => (submissionError = null)}
		/>
	{/if}
</main>
