<script lang="ts">
	import { onMount } from 'svelte';
	import {
		submitFile,
		submitUrl,
		getSubmissionDefaults,
		IngestSubmissionApiError
	} from '$lib/services/ingestSubmissionsApi';
	import type {
		ConvertStepConfig,
		ConvertStepDefinition,
		IngestSubmissionKind,
		SubmissionAcceptedResponse
	} from '$lib/types';
	import StatusBadge from './StatusBadge.svelte';

	type FileKind = Exclude<IngestSubmissionKind, 'url'>;

	let mode: 'url' | 'file' = $state('url');
	let url = $state('');
	let fileKind: FileKind = $state('markdown_file');
	let file: File | null = $state(null);
	let submitting = $state(false);
	let errorMessage: string | null = $state(null);
	let accepted: SubmissionAcceptedResponse | null = $state(null);

	// 004 US2/US3: defaults are the single source of truth for the prompt editor and
	// step toggles (contracts/ingest-submission-api-extension.md GET .../defaults).
	let defaultUserPrompt = $state('');
	let userPromptMaxLength = $state(8000);
	let convertStepDefs: ConvertStepDefinition[] = $state([]);
	let userPrompt = $state('');
	let stepOverrides: ConvertStepConfig = $state({});
	let defaultsError: string | null = $state(null);

	const currentKind = $derived<IngestSubmissionKind>(mode === 'url' ? 'url' : fileKind);

	// Only steps applicable to the currently selected kind are shown (FR-011).
	const applicableSteps = $derived(
		convertStepDefs.filter((step) => step.appliesTo.includes(currentKind))
	);

	onMount(async () => {
		try {
			const defaults = await getSubmissionDefaults();
			defaultUserPrompt = defaults.defaultUserPrompt;
			userPromptMaxLength = defaults.userPromptMaxLength;
			convertStepDefs = defaults.convertSteps;
			userPrompt = defaults.defaultUserPrompt;
			stepOverrides = Object.fromEntries(
				defaults.convertSteps.map((step) => [step.name, step.defaultEnabled])
			);
		} catch {
			// The form still works with system defaults (default prompt / all steps
			// enabled) even if the defaults endpoint is unreachable; only the editable
			// prefill and toggle labels are unavailable.
			defaultsError = 'Could not load prompt/step defaults; using system defaults.';
		}
	});

	function onFileChange(event: Event) {
		const input = event.currentTarget as HTMLInputElement;
		file = input.files?.[0] ?? null;
	}

	function isStepRequired(step: ConvertStepDefinition): boolean {
		return step.requiredFor.includes(currentKind);
	}

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		errorMessage = null;
		accepted = null;

		if (mode === 'url') {
			if (!url.trim()) {
				errorMessage = 'Enter a URL to submit.';
				return;
			}
		} else if (!file) {
			errorMessage = 'Choose a file to submit.';
			return;
		}

		const trimmedPrompt = userPrompt.trim();
		if (trimmedPrompt.length > userPromptMaxLength) {
			errorMessage = `The prompt exceeds the maximum of ${userPromptMaxLength} characters.`;
			return;
		}

		// FR-007: leaving the prompt untouched (or clearing it) must require no extra
		// effort compared to feature 003 — omit the option entirely in that case, which
		// also keeps the call signature identical to the pre-004 API for default submissions.
		const promptOption =
			trimmedPrompt && trimmedPrompt !== defaultUserPrompt.trim() ? trimmedPrompt : undefined;

		const relevantOverrides = Object.fromEntries(
			applicableSteps
				.filter((step) => stepOverrides[step.name] !== step.defaultEnabled)
				.map((step) => [step.name, stepOverrides[step.name]])
		);
		const stepsOption = Object.keys(relevantOverrides).length > 0 ? relevantOverrides : undefined;

		const hasOptions = promptOption !== undefined || stepsOption !== undefined;
		const options = hasOptions
			? { userPrompt: promptOption, convertSteps: stepsOption }
			: undefined;

		submitting = true;
		try {
			accepted =
				mode === 'url'
					? options
						? await submitUrl(url.trim(), options)
						: await submitUrl(url.trim())
					: options
						? await submitFile(fileKind, file!, options)
						: await submitFile(fileKind, file!);
			url = '';
			file = null;
		} catch (error) {
			errorMessage =
				error instanceof IngestSubmissionApiError
					? error.message
					: 'Submission failed unexpectedly.';
		} finally {
			submitting = false;
		}
	}
</script>

<form class="flex flex-col gap-4" onsubmit={handleSubmit} data-testid="submission-form">
	<fieldset class="flex gap-4 text-sm">
		<label class="flex items-center gap-1.5">
			<input type="radio" name="mode" value="url" bind:group={mode} />
			URL
		</label>
		<label class="flex items-center gap-1.5">
			<input type="radio" name="mode" value="file" bind:group={mode} />
			File
		</label>
	</fieldset>

	{#if mode === 'url'}
		<input
			type="url"
			placeholder="https://example.com/article"
			class="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400"
			bind:value={url}
			data-testid="submission-url-input"
		/>
	{:else}
		<div class="flex flex-col gap-2">
			<select
				class="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
				bind:value={fileKind}
				data-testid="submission-kind-select"
			>
				<option value="markdown_file">Markdown</option>
				<option value="pdf_file">PDF</option>
				<option value="office_file">Office document</option>
			</select>
			<input type="file" onchange={onFileChange} data-testid="submission-file-input" />
		</div>
	{/if}

	<div class="flex flex-col gap-1">
		<label for="submission-user-prompt" class="text-sm font-medium text-slate-700">
			User prompt
		</label>
		<textarea
			id="submission-user-prompt"
			rows="3"
			class="rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900"
			bind:value={userPrompt}
			maxlength={userPromptMaxLength}
			data-testid="submission-user-prompt-input"></textarea>
		<p class="text-xs text-slate-400">
			{userPrompt.trim().length} / {userPromptMaxLength} characters. Leave as shown to use the default
			steering.
		</p>
	</div>

	{#if applicableSteps.length > 0}
		<fieldset class="flex flex-col gap-2" data-testid="submission-convert-steps">
			<legend class="text-sm font-medium text-slate-700">Convert steps</legend>
			{#each applicableSteps as step (step.name)}
				{@const required = isStepRequired(step)}
				<label class="flex items-center gap-2 text-sm text-slate-700">
					<input
						type="checkbox"
						checked={stepOverrides[step.name] ?? step.defaultEnabled}
						disabled={required}
						onchange={(e) =>
							(stepOverrides = { ...stepOverrides, [step.name]: e.currentTarget.checked })}
						data-testid={`submission-convert-step-${step.name}`}
					/>
					{step.name}
					{#if required}
						<span class="text-xs text-slate-400"
							>(required for this format — cannot be disabled)</span
						>
					{/if}
				</label>
			{/each}
		</fieldset>
	{/if}

	<button
		type="submit"
		class="self-start rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
		disabled={submitting}
		data-testid="submission-submit-button"
	>
		{submitting ? 'Submitting…' : 'Submit'}
	</button>

	{#if errorMessage}
		<p class="text-sm text-stage-failed" data-testid="submission-error">{errorMessage}</p>
	{/if}

	{#if defaultsError}
		<p class="text-xs text-slate-400" data-testid="submission-defaults-warning">{defaultsError}</p>
	{/if}

	{#if accepted}
		<div class="flex items-center gap-2 text-sm" data-testid="submission-accepted">
			<span>Accepted — task <code>{accepted.taskId}</code> is processing.</span>
			<StatusBadge status={accepted.status} />
		</div>
	{/if}
</form>
