<script lang="ts">
	import ActiveModel from './ActiveModel.svelte';

	// FR-004 unchanged: client-side validation before submission. PROMPT_MAX_LENGTH mirrors the
	// Hub's QuerySubmissionValidator.PromptMaxLength.
	//
	// The design turns this into the thread's composer: the input goes quiet while an answer is
	// streaming and the Ask button becomes Stop ("Stop now lives in the composer … the input is
	// disabled with a 'waiting for the current answer…' placeholder", chat 3), and the model it
	// runs named quietly under it — a row of selectable pills until #149, see $lib/models.ts
	// for why there is nothing left to pick.
	const PROMPT_MAX_LENGTH = 8000;

	interface Props {
		disabled?: boolean;
		onSubmit: (prompt: string) => void | Promise<void>;
		/** Given while a turn is streaming, the Ask button becomes Stop. */
		onStop?: () => void;
	}

	let { disabled = false, onSubmit, onStop }: Props = $props();

	let prompt = $state('');
	// Client-side validation — nothing was sent yet, so this is not a request failure and does
	// not belong in the shared error presentation (024 FR-010 covers request failures).
	let errorMessage: string | null = $state(null);
	let submitting = $state(false);

	const answering = $derived(disabled && !!onStop);

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		errorMessage = null;

		const trimmed = prompt.trim();
		if (!trimmed) {
			errorMessage = 'Enter a question before submitting.';
			return;
		}
		if (trimmed.length > PROMPT_MAX_LENGTH) {
			errorMessage = `The question exceeds the maximum of ${PROMPT_MAX_LENGTH} characters.`;
			return;
		}

		submitting = true;
		try {
			await onSubmit(trimmed);
			prompt = '';
		} finally {
			submitting = false;
		}
	}

	// Ctrl+Enter / Cmd+Enter submits — the standard convention for multi-line submit
	// fields. A bare <textarea> doesn't submit on Enter the way an <input> does, so
	// without this the only way to submit is clicking the button. Plain Enter is left
	// alone and keeps inserting a newline.
	function handleKeydown(event: KeyboardEvent) {
		if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey)) return;
		event.preventDefault();
		(event.currentTarget as HTMLTextAreaElement).form?.requestSubmit();
	}
</script>

<form class="flex flex-col gap-2" onsubmit={handleSubmit} data-testid="query-prompt-form">
	<label for="query-prompt-input" class="sr-only">Ask the wiki a question</label>
	<div class="flex items-end gap-2">
		<textarea
			id="query-prompt-input"
			rows="2"
			class="min-h-14 flex-1 rounded-2xl border border-slate-300 bg-white px-4 py-2 text-sm text-slate-900 disabled:bg-slate-50 disabled:text-slate-400"
			bind:value={prompt}
			maxlength={PROMPT_MAX_LENGTH}
			disabled={disabled || submitting}
			placeholder={answering ? 'waiting for the current answer…' : 'ask a follow-up… ⌘⏎'}
			onkeydown={handleKeydown}
			data-testid="query-prompt-input"></textarea>

		{#if answering}
			<button
				type="button"
				class="rounded-full bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
				onclick={onStop}
				data-testid="query-prompt-stop-button">Stop</button
			>
		{:else}
			<button
				type="submit"
				class="rounded-full bg-blue-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
				disabled={disabled || submitting}
				data-testid="query-prompt-submit-button">{submitting ? 'Asking…' : 'Ask'}</button
			>
		{/if}
	</div>

	<ActiveModel testId="ask-active-model" />

	<!-- 011 T020/FR-008: the rule and the context promise are stated whether or not a turn is
	     running — the design keeps this as a permanent footnote under the composer, so nobody
	     has to trigger the disabled state to learn how follow-ups behave. -->
	<p class="text-xs text-slate-400" data-testid="query-context-hint">
		One question at a time — wait for the answer, or stop it, before asking another. Follow-ups keep
		everything asked and answered so far in this conversation; a new conversation clears it.
	</p>

	{#if errorMessage}
		<p class="text-sm text-stage-failed" data-testid="query-prompt-error">{errorMessage}</p>
	{/if}
</form>
