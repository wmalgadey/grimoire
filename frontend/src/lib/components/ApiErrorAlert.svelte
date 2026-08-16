<script lang="ts">
	/**
	 * 024-api-error-presentation (ADR-026): the one presentation every surface uses for a request
	 * failure. Before this, eleven components and routes each rendered their own
	 * `<p class="text-sm text-stage-failed">` — one line of small text with no category, no
	 * technical detail, and no way to retry.
	 *
	 * Hand-built rather than pulled from a widget library, per ADR-001's recorded reason for
	 * choosing this stack: "a small set of central, reusable UI components governed by shared CSS,
	 * rather than many one-off, inconsistently styled views".
	 *
	 * The component does not know how to retry anything. It raises `onRetry`, and each surface
	 * supplies the action — that is what keeps it free of surface-specific knowledge and usable
	 * everywhere (FR-010).
	 */
	import type { PresentedError } from '$lib/services/apiError';

	interface Props {
		error: PresentedError;
		/** Supplied by surfaces that can re-run the failed action. Ignored unless retryable. */
		onRetry?: () => void;
		/** Supplied by surfaces that let the user clear the error. */
		onDismiss?: () => void;
		testId?: string;
	}

	let { error, onRetry, onDismiss, testId = 'api-error' }: Props = $props();

	let detailsOpen = $state(false);

	const showRetry = $derived(error.retryable && onRetry !== undefined);
	const hasTechnicalDetail = $derived(
		error.status !== null ||
			error.code !== null ||
			error.traceId !== null ||
			error.bodyExcerpt !== null ||
			error.fullMessage !== null
	);
</script>

<!--
	role="alert" announces the failure to assistive technology without moving keyboard focus, so a
	user typing in a form hears what happened and keeps their place (FR-009).
-->
<div
	role="alert"
	data-testid={testId}
	data-category={error.category}
	class="rounded-md border-l-4 bg-slate-50 p-3 text-slate-800"
	class:border-red-500={error.category === 'fault' || error.category === 'unexpected'}
	class:border-amber-500={error.category === 'declined'}
	class:border-slate-400={error.category === 'unreachable'}
>
	<div class="flex items-start justify-between gap-3">
		<div class="min-w-0">
			<p class="font-semibold" data-testid="{testId}-title">{error.title}</p>
			<p class="mt-0.5 text-sm" data-testid="{testId}-message">{error.message}</p>
		</div>

		{#if onDismiss}
			<button
				type="button"
				class="shrink-0 text-slate-500 hover:text-slate-800"
				data-testid="{testId}-dismiss"
				aria-label="Dismiss this error"
				onclick={onDismiss}
			>
				×
			</button>
		{/if}
	</div>

	<div class="mt-2 flex items-center gap-3">
		{#if showRetry}
			<button
				type="button"
				class="rounded border border-slate-300 px-2 py-1 text-sm hover:bg-slate-100"
				data-testid="{testId}-retry"
				onclick={onRetry}
			>
				Try again
			</button>
		{/if}

		{#if hasTechnicalDetail}
			<button
				type="button"
				class="text-xs text-slate-500 underline hover:text-slate-800"
				data-testid="{testId}-details-toggle"
				aria-expanded={detailsOpen}
				onclick={() => (detailsOpen = !detailsOpen)}
			>
				{detailsOpen ? 'Hide technical details' : 'Technical details'}
			</button>
		{/if}
	</div>

	<!--
		Rendered only when opened, so the technical facts genuinely do not occupy the primary
		message area (FR-006) — a hidden-but-present node would still be read by a screen reader
		walking the region.
	-->
	{#if detailsOpen && hasTechnicalDetail}
		<dl
			class="mt-2 space-y-1 border-t border-slate-200 pt-2 text-xs"
			data-testid="{testId}-details"
		>
			{#if error.status !== null}
				<div class="flex gap-2">
					<dt class="text-slate-500">Status</dt>
					<dd data-testid="{testId}-detail-status">{error.status}</dd>
				</div>
			{/if}
			{#if error.code !== null}
				<div class="flex gap-2">
					<dt class="text-slate-500">Code</dt>
					<dd data-testid="{testId}-detail-code">{error.code}</dd>
				</div>
			{/if}
			{#if error.traceId !== null}
				<div class="flex gap-2">
					<dt class="text-slate-500">Trace</dt>
					<dd class="break-all" data-testid="{testId}-detail-trace-id">{error.traceId}</dd>
				</div>
			{/if}
			{#if error.fullMessage !== null}
				<div>
					<dt class="text-slate-500">Full message</dt>
					<dd class="break-words" data-testid="{testId}-detail-full-message">
						{error.fullMessage}
					</dd>
				</div>
			{/if}
			{#if error.bodyExcerpt !== null}
				<div>
					<dt class="text-slate-500">Response</dt>
					<dd class="break-all whitespace-pre-wrap" data-testid="{testId}-detail-body">
						{error.bodyExcerpt}
					</dd>
				</div>
			{/if}
		</dl>
	{/if}
</div>
