<script lang="ts">
	import SubmissionForm from './SubmissionForm.svelte';

	// The design moves ingest off the board and into a dialog behind "+ Ingest", so the board
	// itself is the whole screen. The form inside is unchanged — it still owns validation,
	// defaults and submission; the dialog only frames it and closes once a submission is
	// accepted, at which point the new task is already on the board via the lifecycle stream.
	interface Props {
		open: boolean;
		onClose: () => void;
		onAccepted?: () => void;
	}

	let { open, onClose, onAccepted }: Props = $props();
</script>

<svelte:window
	onkeydown={(event) => {
		if (open && event.key === 'Escape') onClose();
	}}
/>

{#if open}
	<!-- Clicking the backdrop dismisses ("if you click on the backdrop, the popover should be
	     closed" — chat 3); the panel stops the click so an in-panel click never dismisses. -->
	<div
		class="fixed inset-0 z-50 grid place-items-center bg-slate-900/40 p-4"
		data-testid="ingest-dialog-backdrop"
		onclick={onClose}
		onkeydown={() => {}}
		role="presentation"
	>
		<div
			class="flex max-h-[90vh] w-full max-w-xl flex-col gap-4 overflow-y-auto rounded-lg bg-white p-6 shadow-xl"
			role="dialog"
			tabindex="-1"
			aria-modal="true"
			aria-labelledby="ingest-dialog-title"
			data-testid="ingest-dialog"
			onclick={(event) => event.stopPropagation()}
			onkeydown={() => {}}
		>
			<div class="flex flex-col gap-1">
				<h2 id="ingest-dialog-title" class="text-lg font-semibold text-slate-900">
					Submit a source
				</h2>
				<p class="text-sm text-slate-500">
					Submit a URL, Markdown, PDF, or Office document to ingest into the wiki. Its progress
					appears on the board as soon as it's accepted.
				</p>
			</div>

			<SubmissionForm onAccepted={() => onAccepted?.()} />

			<div class="flex justify-end">
				<button
					type="button"
					class="rounded border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
					onclick={onClose}
					data-testid="ingest-dialog-close">Close</button
				>
			</div>
		</div>
	</div>
{/if}
