<script lang="ts">
	import DOMPurify from 'dompurify';
	import { marked } from 'marked';
	import {
		attachRemediationTaskContext,
		sendRemediationTaskMessage,
		RemediationApiError
	} from '$lib/services/remediationApi';
	import type {
		RemediationTaskAttachedContext,
		RemediationTaskMessage,
		RemediationTaskState
	} from '$lib/types';

	// 015-lint-board-parity T043 (US5, FR-011/FR-012/FR-014): the task detail view's
	// message thread — attached context (visible in every state, FR-014) with its own
	// attach form (visible only while `proposed`, FR-011), and the human⇄agent message
	// thread (visible in every state, FR-014) with its own send box (visible only while
	// `proposed`, FR-012 — messaging exists to steer the proposal before authorization).
	// Mirrors QueryConversation.svelte's marked+DOMPurify rendering for agent replies and
	// RemediationTaskCard.svelte's self-contained call-the-API-directly pattern: this
	// component never mutates `messages`/`attachedContext` locally on success — the
	// parent's live `messages`/detail fetch (driven by `remediationTaskLifecycleChanged`/
	// `remediationMessageTurnChanged`) is the single source of truth.
	const CONTENT_MAX_LENGTH = 8000;

	interface Props {
		taskId: string;
		taskState: RemediationTaskState;
		attachedContext: RemediationTaskAttachedContext[];
		messages: RemediationTaskMessage[];
		messageTurnActive: boolean;
	}

	let { taskId, taskState, attachedContext, messages, messageTurnActive }: Props = $props();

	let contextInput = $state('');
	let contextBusy = $state(false);
	let contextError: string | null = $state(null);

	let messageInput = $state('');
	let messageBusy = $state(false);
	let messageError: string | null = $state(null);

	const canCompose = $derived(taskState === 'proposed');

	// Agent replies are markdown; human messages are rendered as plain text (they are
	// exactly what the human typed, no formatting expected) — same sanitization
	// discipline as QueryConversation.svelte's renderAnswer for untrusted content.
	function renderContent(text: string): string {
		return DOMPurify.sanitize(marked.parse(text, { async: false }) as string);
	}

	async function handleAttachContext(event: SubmitEvent) {
		event.preventDefault();
		contextError = null;

		const trimmed = contextInput.trim();
		if (!trimmed) {
			contextError = 'Enter some context before attaching it.';
			return;
		}
		if (trimmed.length > CONTENT_MAX_LENGTH) {
			contextError = `The attached context exceeds the maximum of ${CONTENT_MAX_LENGTH} characters.`;
			return;
		}

		contextBusy = true;
		try {
			await attachRemediationTaskContext(taskId, trimmed);
			contextInput = '';
		} catch (err) {
			contextError =
				err instanceof RemediationApiError
					? err.message
					: 'The request failed unexpectedly. Please try again.';
		} finally {
			contextBusy = false;
		}
	}

	async function handleSendMessage(event: SubmitEvent) {
		event.preventDefault();
		messageError = null;

		const trimmed = messageInput.trim();
		if (!trimmed) {
			messageError = 'Enter a message before sending.';
			return;
		}
		if (trimmed.length > CONTENT_MAX_LENGTH) {
			messageError = `The message exceeds the maximum of ${CONTENT_MAX_LENGTH} characters.`;
			return;
		}

		messageBusy = true;
		try {
			await sendRemediationTaskMessage(taskId, trimmed);
			messageInput = '';
		} catch (err) {
			messageError =
				err instanceof RemediationApiError
					? err.message
					: 'The request failed unexpectedly. Please try again.';
		} finally {
			messageBusy = false;
		}
	}
</script>

<section class="flex flex-col gap-4" data-testid="task-message-thread">
	<div class="flex flex-col gap-2">
		<h2 class="text-sm font-semibold text-slate-700">Attached context</h2>
		{#if attachedContext.length > 0}
			<ul class="flex flex-col gap-2" data-testid="task-message-thread-context-list">
				{#each attachedContext as entry (entry.attachedAt)}
					<li
						class="rounded border border-slate-200 bg-slate-50 p-2 text-sm text-slate-700"
						data-testid="task-message-thread-context-item"
					>
						{entry.content}
					</li>
				{/each}
			</ul>
		{:else}
			<p class="text-xs text-slate-400" data-testid="task-message-thread-context-empty">
				No context attached yet.
			</p>
		{/if}

		{#if canCompose}
			<form
				class="flex flex-col gap-2"
				onsubmit={handleAttachContext}
				data-testid="task-message-thread-context-form"
			>
				<textarea
					rows="2"
					class="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 disabled:opacity-50"
					bind:value={contextInput}
					maxlength={CONTENT_MAX_LENGTH}
					disabled={contextBusy}
					placeholder="Add information or instructions for the agent before authorizing…"
					data-testid="task-message-thread-context-input"></textarea>
				<button
					type="submit"
					class="self-start rounded border border-slate-300 px-3 py-1 text-xs font-medium text-slate-600 disabled:opacity-50"
					disabled={contextBusy}
					data-testid="task-message-thread-context-submit"
				>
					{contextBusy ? 'Attaching…' : 'Attach context'}
				</button>
				{#if contextError}
					<p class="text-xs text-red-700" data-testid="task-message-thread-context-error">
						{contextError}
					</p>
				{/if}
			</form>
		{/if}
	</div>

	<div class="flex flex-col gap-2">
		<h2 class="text-sm font-semibold text-slate-700">Messages</h2>
		{#if messages.length > 0}
			<div class="flex flex-col gap-2">
				{#each messages as message, index (index)}
					<article
						class="rounded border p-2 text-sm"
						class:border-slate-200={message.sender === 'human'}
						class:bg-slate-50={message.sender === 'human'}
						class:border-blue-200={message.sender === 'agent'}
						class:bg-blue-50={message.sender === 'agent'}
						data-testid="task-message-thread-message"
						data-sender={message.sender}
					>
						<p class="mb-1 text-xs font-medium text-slate-500">
							{message.sender === 'human' ? 'You' : 'Agent'}
						</p>
						<div class="text-slate-800">
							<!-- eslint-disable-next-line svelte/no-at-html-tags -->
							{@html renderContent(message.content)}
						</div>
					</article>
				{/each}
			</div>
		{:else}
			<p class="text-xs text-slate-400" data-testid="task-message-thread-messages-empty">
				No messages yet.
			</p>
		{/if}

		{#if messageTurnActive}
			<p class="text-xs text-slate-400" data-testid="task-message-thread-turn-active">
				The agent is responding…
			</p>
		{/if}

		{#if canCompose}
			<form
				class="flex flex-col gap-2"
				onsubmit={handleSendMessage}
				data-testid="task-message-thread-send-form"
			>
				<textarea
					rows="2"
					class="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 disabled:opacity-50"
					bind:value={messageInput}
					maxlength={CONTENT_MAX_LENGTH}
					disabled={messageBusy || messageTurnActive}
					placeholder="Ask the agent about this proposal…"
					data-testid="task-message-thread-send-input"></textarea>
				<button
					type="submit"
					class="self-start rounded bg-blue-600 px-3 py-1 text-xs font-medium text-white disabled:opacity-50"
					disabled={messageBusy || messageTurnActive}
					data-testid="task-message-thread-send-button"
				>
					{messageBusy ? 'Sending…' : 'Send'}
				</button>
				{#if messageError}
					<p class="text-xs text-red-700" data-testid="task-message-thread-send-error">
						{messageError}
					</p>
				{/if}
			</form>
		{/if}
	</div>
</section>
