<script lang="ts">
	import { onDestroy, onMount } from 'svelte';
	import { SvelteMap, SvelteSet } from 'svelte/reactivity';
	import AppNav from '$lib/components/AppNav.svelte';
	import ApiErrorAlert from '$lib/components/ApiErrorAlert.svelte';
	import ConversationList from '$lib/components/ConversationList.svelte';
	import QueryConversation from '$lib/components/QueryConversation.svelte';
	import QueryPromptForm from '$lib/components/QueryPromptForm.svelte';
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
	import { activeTurnId, conversations } from '$lib/stores/conversations.svelte';
	import { citationNote, extractConversationCitations, obsidianUri } from '$lib/wikiLinks';
	import type { ConnectionState, QueryTurn, QueryTurnStatus } from '$lib/types';

	// Ask, as the design leaves it (5c plus the overview added in chat 3): the tab lands on a
	// list of conversations, opening one shows the thread with a rail of the pages it touched,
	// and "+ Ask" in the nav starts a fresh one.
	//
	// The turn lifecycle below is the pre-design one, moved from component state onto the
	// conversation store so more than one conversation can be held at a time. The Hub still
	// sources follow-up context from its own Conversation Record (ADR-014) — nothing here
	// changes what is submitted.

	let view: 'list' | 'thread' = $state('list');
	let connectionState: ConnectionState = $state('connecting');
	let submissionError: PresentedError | null = $state(null);
	let interruptError: PresentedError | null = $state(null);
	// Kept so the retry affordances can re-run exactly what failed (FR-008).
	let lastSubmittedPrompt: string | null = $state(null);
	let lastInterruptedTurnId: string | null = $state(null);

	let client: QueryLifecycleClient | undefined;
	const seenTurnChangedKeys = new SvelteSet<string>();
	const lastAppliedSequenceByTurnId = new SvelteMap<string, number>();

	const active = $derived(conversations.active);
	const turns = $derived(active?.turns ?? []);
	const runningTurnId = $derived(activeTurnId(active));
	const railPages = $derived(extractConversationCitations(turns.map((t) => t.answer)));

	function openConversation(id: string) {
		conversations.open(id);
		view = 'thread';
	}

	function startConversation() {
		conversations.create();
		view = 'thread';
		submissionError = null;
		interruptError = null;
	}

	async function handleSubmit(prompt: string) {
		const conversation = conversations.active;
		if (!conversation) return;
		submissionError = null;
		lastSubmittedPrompt = prompt;

		// ADR-014: the submission carries only the prompt.
		try {
			const accepted = await submitQueryTurn(conversation.id, prompt);
			const turn: QueryTurn = {
				turnId: accepted.turnId,
				conversationId: accepted.conversationId,
				position: accepted.position,
				prompt,
				answer: '',
				state: accepted.state
			};
			conversations.addTurn(conversation.id, turn);
			lastAppliedSequenceByTurnId.set(accepted.turnId, 0);
		} catch (error) {
			submissionError = toPresentedError(error);
		}
	}

	function retrySubmit() {
		if (lastSubmittedPrompt !== null) void handleSubmit(lastSubmittedPrompt);
	}

	// 024 SC-005: stopping a turn is a user action, so its failure belongs in the shared
	// presentation — an interrupt that never reached the Hub produces no `queryTurnChanged`,
	// so the answer keeps streaming and the click is otherwise indistinguishable from a no-op.
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

	// On reconnect, refresh the authoritative state via REST before resuming the stream
	// (contracts/query-conversation-api.md ## Rules).
	async function refreshTurn(turnId: string) {
		try {
			const authoritative = await getQueryTurn(turnId);
			conversations.updateTurn(turnId, (turn) => ({
				...turn,
				answer: authoritative.answer,
				state: authoritative.state,
				failureReason: authoritative.failureReason
			}));
		} catch {
			// Best-effort reconciliation; subsequent lifecycle events still apply normally.
		}
	}

	// spec.md Edge Cases: an in-flight turn at reload time is treated as interrupted.
	// `pagehide` fires reliably on reload/navigation/tab-close, and `keepalive: true` lets the
	// request complete as the page unloads. Every conversation's running turn is covered, not
	// just the one on screen.
	function handlePageHide() {
		for (const turnId of conversations.runningTurnIds) {
			// 024 FR-011: silence is deliberate — the page is unloading, so there is no surface
			// left to present a failure on, but the promise still needs settling.
			void interruptQueryTurn(turnId, (input, init) =>
				fetch(input, { ...init, keepalive: true })
			).catch(() => {});
		}
	}

	onMount(() => {
		client = createQueryLifecycleClient();

		client.onAnswerChunk((event) => {
			const lastSequence = lastAppliedSequenceByTurnId.get(event.turnId) ?? 0;
			conversations.updateTurn(event.turnId, (turn) => {
				const { answer, lastAppliedSequence } = applyAnswerChunk(turn.answer, event, lastSequence);
				lastAppliedSequenceByTurnId.set(event.turnId, lastAppliedSequence);
				return { ...turn, answer };
			});
		});

		client.onTurnChanged((event) => {
			if (!applyTurnChanged(event, seenTurnChangedKeys)) return;

			conversations.updateTurn(event.turnId, (turn) => ({
				...turn,
				state: event.toState as QueryTurnStatus,
				failureReason: event.failureReason
			}));
		});

		client.onConnectionStateChanged((state) => {
			connectionState = state;
		});

		client.onReconnected(() => {
			for (const turnId of conversations.runningTurnIds) void refreshTurn(turnId);
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
	<title>Conversations — Grimoire</title>
</svelte:head>

<div class="flex min-h-screen flex-col bg-white">
	<AppNav current="conversations" {connectionState} onNewConversation={startConversation} />

	{#if view === 'list'}
		<div class="flex flex-1 flex-col gap-4 px-6 py-6">
			<div class="flex items-baseline gap-3">
				<h1 class="text-xs font-semibold tracking-wider text-slate-600 uppercase">Conversations</h1>
				<span class="text-xs text-slate-400" data-testid="conversation-summary">
					{conversations.list.length}
					{conversations.list.length === 1 ? 'conversation' : 'conversations'}
				</span>
			</div>

			{#if conversations.list.length === 0}
				<p class="max-w-lg text-sm text-slate-500" data-testid="conversation-list-empty">
					No conversations yet. Start one with <span class="font-medium">+ Ask</span> and the answer streams
					in, grounded in wiki content.
				</p>
			{:else}
				<ConversationList conversations={conversations.list} onOpen={openConversation} />
			{/if}

			<p class="max-w-lg text-xs text-slate-400">
				Each conversation keeps its own context. Open one to follow up inside it, or start a new
				conversation from the nav. Conversations live in this browser session only.
			</p>
		</div>
	{:else}
		<div class="flex flex-1 items-stretch">
			<div class="flex min-w-0 flex-1 flex-col gap-4 border-r border-slate-200 px-6 py-6">
				<button
					type="button"
					class="self-start text-sm text-slate-500 underline underline-offset-2 hover:text-slate-900"
					onclick={() => (view = 'list')}
					data-testid="back-to-conversations">← Conversations</button
				>

				{#if turns.length === 0}
					<p class="max-w-lg text-sm text-slate-500" data-testid="thread-empty">
						New conversation. Ask a question and the answer streams in, grounded in wiki content.
					</p>
				{/if}

				<QueryConversation {turns} />

				{#if interruptError}
					<ApiErrorAlert
						error={interruptError}
						testId="query-interrupt-error"
						onRetry={retryInterrupt}
						onDismiss={() => (interruptError = null)}
					/>
				{/if}

				<div class="mt-auto flex flex-col gap-2">
					<QueryPromptForm
						disabled={runningTurnId !== null}
						onSubmit={handleSubmit}
						onStop={runningTurnId ? () => void handleInterrupt(runningTurnId) : undefined}
						model={active?.model}
						onModelChange={(model) => active && conversations.setModel(active.id, model)}
					/>

					{#if submissionError}
						<ApiErrorAlert
							error={submissionError}
							testId="query-submission-error"
							onRetry={retrySubmit}
							onDismiss={() => (submissionError = null)}
						/>
					{/if}
				</div>
			</div>

			<aside class="flex w-64 shrink-0 flex-col gap-2 px-5 py-6" data-testid="conversation-rail">
				<h2 class="text-xs font-semibold tracking-wider text-slate-600 uppercase">
					This conversation
				</h2>
				{#each railPages as page (page.page)}
					<div class="flex flex-col gap-0.5 rounded-lg bg-slate-50 p-3">
						<!-- An `obsidian://` URI is not a SvelteKit route, so resolve() does not apply. -->
						<!-- eslint-disable svelte/no-navigation-without-resolve -->
						<a
							href={obsidianUri(page.page)}
							class="text-sm font-medium text-slate-900 hover:underline"
							data-testid="conversation-rail-page">{page.page} ↗</a
						>
						<!-- eslint-enable svelte/no-navigation-without-resolve -->
						<span class="text-xs text-slate-400">{citationNote(page.count)}</span>
					</div>
				{/each}
				{#if railPages.length === 0}
					<p class="text-xs text-slate-400" data-testid="conversation-rail-empty">
						Pages an answer cites appear here, ready to open in Obsidian.
					</p>
				{/if}
			</aside>
		</div>
	{/if}
</div>
