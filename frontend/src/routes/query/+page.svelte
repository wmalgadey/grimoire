<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { SvelteMap, SvelteSet } from 'svelte/reactivity';
	import { resolve } from '$app/paths';
	import ConnectionStatusIndicator from '$lib/components/ConnectionStatusIndicator.svelte';
	import QueryConversation from '$lib/components/QueryConversation.svelte';
	import QueryPromptForm from '$lib/components/QueryPromptForm.svelte';
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
	let submissionError: string | null = $state(null);

	let client: QueryLifecycleClient | undefined;
	const seenTurnChangedKeys = new SvelteSet<string>();
	const lastAppliedSequenceByTurnId = new SvelteMap<string, number>();

	function updateTurn(turnId: string, update: (turn: QueryTurn) => QueryTurn) {
		turns = turns.map((t) => (t.turnId === turnId ? update(t) : t));
	}

	async function handleSubmit(prompt: string) {
		submissionError = null;

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
			submissionError =
				error instanceof Error ? error.message : 'Failed to submit the question unexpectedly.';
		}
	}

	async function handleInterrupt(turnId: string) {
		try {
			await interruptQueryTurn(turnId);
		} catch {
			// The turn's actual state arrives via queryTurnChanged regardless; nothing
			// else to do client-side if the interrupt call itself failed to reach the Hub.
		}
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
		void interruptQueryTurn(activeTurnId, (input, init) =>
			fetch(input, { ...init, keepalive: true })
		);
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

	<QueryPromptForm disabled={activeTurnId !== null} onSubmit={handleSubmit} />

	{#if submissionError}
		<p class="text-sm text-stage-failed" data-testid="query-submission-error">{submissionError}</p>
	{/if}
</main>
