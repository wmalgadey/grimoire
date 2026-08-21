<script lang="ts">
	import { presentRecordedFailure } from '$lib/services/apiError';
	import type { BoardItem } from '$lib/board';

	// One card shape for every kind of work on the board. The design deliberately collapses
	// ingest's card, the violet lint card and the amber remediation card into a single object —
	// what kind of work it is reads from the title, the note and the tag, not from a colour
	// scheme per kind — and moves every action off the card into the popover it opens
	// ("that view is too cluttered, the pop over is nice for a quick view", chat 1).
	interface Props {
		item: BoardItem;
		onOpen: (item: BoardItem, anchor: HTMLElement) => void;
	}

	let { item, onOpen }: Props = $props();

	// 024 FR-012: presentation only — the recorded reason is untouched, just shortened to the
	// one sentence a card has room for. Its full text with technical detail is in the popover.
	const failureLine = $derived(
		item.failureReason ? presentRecordedFailure(item.failureReason).message : null
	);

	const tagClasses: Record<string, string> = {
		Proposed: 'bg-amber-100 text-amber-800',
		Waiting: 'bg-sky-100 text-sky-700',
		Executing: 'bg-blue-100 text-blue-700',
		'Health check': 'bg-violet-100 text-violet-700',
		'Not applicable': 'bg-slate-100 text-slate-600',
		Dismissed: 'bg-slate-100 text-slate-600'
	};
</script>

<button
	type="button"
	class="flex w-full flex-col gap-1.5 rounded-lg border border-slate-200 bg-white p-3 text-left shadow-sm hover:border-blue-400 hover:shadow"
	onclick={(event) => onOpen(item, event.currentTarget)}
	data-testid="board-card"
	data-kind={item.kind}
	data-task-id={item.id}
>
	<span class="flex items-center gap-2">
		{#if item.lane === 'running'}
			<span
				class="h-1.5 w-1.5 shrink-0 animate-pulse rounded-full bg-blue-500"
				data-testid="board-card-live-dot"
				aria-hidden="true"
			></span>
		{/if}
		<span
			class="truncate text-sm font-medium text-slate-900"
			title={item.title}
			data-testid="board-card-title">{item.title}</span
		>
	</span>

	<!-- #130: the id line is suppressed when it is the title, so the last-resort case degrades
	     to one line instead of printing the same string twice and reading as broken. A task
	     only falls all the way through the label chain now if it has no manifest and no
	     submitted filename or URL either. -->
	{#if item.title !== item.id}
		<span class="truncate font-mono text-xs text-slate-400" data-testid="board-card-id"
			>{item.id}</span
		>
	{/if}

	{#if item.note}
		<span class="text-xs text-slate-500" data-testid="board-card-note">{item.note}</span>
	{/if}

	{#if failureLine}
		<span class="line-clamp-2 text-xs text-red-600" data-testid="board-card-failure-reason"
			>{failureLine}</span
		>
	{/if}

	<span class="flex items-center gap-2 text-xs text-slate-400">
		{#if item.tagLabel}
			<span
				class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium {tagClasses[
					item.tagLabel
				] ?? 'bg-slate-100 text-slate-600'}"
				data-testid="board-card-tag">{item.tagLabel}</span
			>
		{/if}
		<time datetime={item.updatedAt}>{new Date(item.updatedAt).toLocaleTimeString()}</time>
	</span>
</button>
