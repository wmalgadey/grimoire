<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import ConnectionStatusIndicator from '$lib/components/ConnectionStatusIndicator.svelte';
	import QueryConversation from '$lib/components/QueryConversation.svelte';
	import QueryPromptForm from '$lib/components/QueryPromptForm.svelte';
	import {
		applyAnswerChunk,
		applyTurnChanged,
		createQueryLifecycleClient,
		type QueryLifecycleClient
	} from '$lib/services/queryLifecycleClient';
	import { interruptQueryTurn, submitQueryTurn } from '$lib/services/querySubmissionApi';
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
	const seenTurnChangedKeys = new Set<string>();
	const lastAppliedSequenceByTurnId = new Map<string, number>();

	function updateTurn(turnId: string, update: (turn: QueryTurn) => QueryTurn) {
		turns = turns.map((t) => (t.turnId === turnId ? update(t) : t));
	}

	async function handleSubmit(prompt: string) {
		submissionError = null;

		// US3 groundwork (inert for a single-turn conversation): every prior turn,
		// including partial answers of interrupted ones, goes with every submission.
		const priorTurns = turns.map((t) => ({
			position: t.position,
			prompt: t.prompt,
			answer: t.answer,
			state: t.state
		}));

		try {
			const accepted = await submitQueryTurn(conversationId, prompt, priorTurns);
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

	function startNewConversation() {
		conversationId = newConversationId();
		turns = [];
		activeTurnId = null;
		lastAppliedSequenceByTurnId.clear();
		seenTurnChangedKeys.clear();
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

		void client.start();
	});

	onDestroy(() => {
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
	</header>

	<QueryConversation {turns} onInterrupt={handleInterrupt} />

	<QueryPromptForm disabled={activeTurnId !== null} onSubmit={handleSubmit} />

	{#if submissionError}
		<p class="text-sm text-stage-failed" data-testid="query-submission-error">{submissionError}</p>
	{/if}
</main>
