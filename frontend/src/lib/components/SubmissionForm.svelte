<script lang="ts">
	import {
		submitFile,
		submitUrl,
		IngestSubmissionApiError
	} from '$lib/services/ingestSubmissionsApi';
	import type { IngestSubmissionKind, SubmissionAcceptedResponse } from '$lib/types';
	import StatusBadge from './StatusBadge.svelte';

	type FileKind = Exclude<IngestSubmissionKind, 'url'>;

	let mode: 'url' | 'file' = $state('url');
	let url = $state('');
	let fileKind: FileKind = $state('markdown_file');
	let file: File | null = $state(null);
	let submitting = $state(false);
	let errorMessage: string | null = $state(null);
	let accepted: SubmissionAcceptedResponse | null = $state(null);

	function onFileChange(event: Event) {
		const input = event.currentTarget as HTMLInputElement;
		file = input.files?.[0] ?? null;
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

		submitting = true;
		try {
			accepted = mode === 'url' ? await submitUrl(url.trim()) : await submitFile(fileKind, file!);
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

	{#if accepted}
		<div class="flex items-center gap-2 text-sm" data-testid="submission-accepted">
			<span>Accepted — task <code>{accepted.taskId}</code> is processing.</span>
			<StatusBadge status={accepted.status} />
		</div>
	{/if}
</form>
