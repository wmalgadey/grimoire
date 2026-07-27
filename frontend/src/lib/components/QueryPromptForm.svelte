<script lang="ts">
	// FR-004: client-side validation before submission, mirrors SubmissionForm.svelte's
	// pattern. PROMPT_MAX_LENGTH mirrors the Hub's QuerySubmissionValidator.PromptMaxLength
	// (no /defaults-style endpoint for Query — there is no editable default prompt to fetch).
	const PROMPT_MAX_LENGTH = 8000;

	interface Props {
		disabled?: boolean;
		onSubmit: (prompt: string) => void | Promise<void>;
	}

	let { disabled = false, onSubmit }: Props = $props();

	let prompt = $state('');
	let errorMessage: string | null = $state(null);
	let submitting = $state(false);

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
	<label for="query-prompt-input" class="text-sm font-medium text-slate-700">
		Ask the wiki a question
	</label>
	<textarea
		id="query-prompt-input"
		rows="3"
		class="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 disabled:opacity-50"
		bind:value={prompt}
		maxlength={PROMPT_MAX_LENGTH}
		disabled={disabled || submitting}
		onkeydown={handleKeydown}
		data-testid="query-prompt-input"></textarea>

	{#if disabled}
		<p class="text-xs text-slate-400" data-testid="query-prompt-disabled-hint">
			One question at a time — wait for the current answer to finish (or stop it) before asking
			another.
		</p>
	{/if}

	<div class="flex items-center gap-2">
		<button
			type="submit"
			class="self-start rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
			disabled={disabled || submitting}
			data-testid="query-prompt-submit-button"
		>
			{submitting ? 'Asking…' : 'Ask'}
		</button>
	</div>

	{#if errorMessage}
		<p class="text-sm text-stage-failed" data-testid="query-prompt-error">{errorMessage}</p>
	{/if}
</form>
