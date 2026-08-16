<script lang="ts">
	import ApiErrorAlert from './ApiErrorAlert.svelte';
	import ModelPicker from './ModelPicker.svelte';
	import { triggerLintRun } from '$lib/services/lintApi';
	import { toPresentedError, type PresentedError } from '$lib/services/apiError';
	import { DEFAULT_LINT_MODEL, MODEL_NOTES, MODELS } from '$lib/models';

	// 015 FR-002/SC-003 kept: a lint run is still triggered in one action from the shell, and a
	// blocked trigger still surfaces its reason (SC-004). What changed is where the run then
	// shows up — with the /lint route gone, the run is an ordinary board card, so this popover
	// only starts it ("Run Lint — same popover pattern, model pick then Run Lint, which files
	// the health check on the board", chat 3).
	let open = $state(false);
	let triggering = $state(false);
	let triggerError: PresentedError | null = $state(null);
	let model = $state(DEFAULT_LINT_MODEL);

	async function handleTrigger() {
		triggering = true;
		triggerError = null;
		try {
			// The 202 is enough: the board's own lint stream is the source of truth for the run
			// that results, exactly as it was when the board owned this button.
			await triggerLintRun();
			open = false;
		} catch (err) {
			triggerError = toPresentedError(err);
		} finally {
			triggering = false;
		}
	}
</script>

<svelte:window
	onkeydown={(event) => {
		if (open && event.key === 'Escape') open = false;
	}}
/>

<div class="relative">
	<button
		type="button"
		class="rounded border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
		aria-expanded={open}
		onclick={() => (open = !open)}
		data-testid="nav-lint-button">Run Lint</button
	>

	{#if open}
		<!-- Backdrop first, panel above it: a click anywhere else dismisses (chat 3). -->
		<div
			class="fixed inset-0 z-40"
			onclick={() => (open = false)}
			onkeydown={() => {}}
			role="presentation"
			data-testid="lint-popover-backdrop"
		></div>
		<div
			class="absolute right-0 z-50 mt-2 flex w-80 flex-col gap-2 rounded-lg border border-slate-200 bg-white p-4 text-left shadow-lg"
			data-testid="lint-popover"
		>
			<span class="text-sm font-semibold text-slate-900">Run a wiki health check</span>
			<p class="text-xs text-slate-500">
				Lint reads the whole wiki and files its findings as tasks on the board.
			</p>

			<span class="mt-1 text-xs font-medium tracking-wide text-slate-500 uppercase">Model</span>
			<ModelPicker
				models={MODELS}
				selected={model}
				notes={MODEL_NOTES}
				direction="column"
				onSelect={(picked) => (model = picked)}
				testId="lint-model-picker"
			/>

			{#if triggerError}
				<ApiErrorAlert
					error={triggerError}
					testId="lint-trigger-error"
					onRetry={handleTrigger}
					onDismiss={() => (triggerError = null)}
				/>
			{/if}

			<div class="mt-1 flex justify-end gap-2">
				<button
					type="button"
					class="rounded border border-slate-300 px-3 py-1 text-sm text-slate-700 hover:bg-slate-50"
					onclick={() => (open = false)}
					data-testid="lint-popover-cancel">Cancel</button
				>
				<button
					type="button"
					class="rounded bg-blue-600 px-3 py-1 text-sm font-medium text-white disabled:opacity-50"
					disabled={triggering}
					onclick={handleTrigger}
					data-testid="lint-trigger-button">{triggering ? 'Triggering…' : 'Run Lint'}</button
				>
			</div>
		</div>
	{/if}
</div>
