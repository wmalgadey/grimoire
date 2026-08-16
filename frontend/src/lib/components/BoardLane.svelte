<script lang="ts">
	import BoardCard from './BoardCard.svelte';
	import { LANE_LABELS, type BoardItem } from '$lib/board';
	import type { LifecycleStage } from '$lib/types';

	// A stage column that can be put away. "There should be icons to collapse and expand on the
	// columns" (chat 3): collapsed, the lane becomes a narrow rail carrying its name and count,
	// which is what keeps Done and Failed present without spending width on them.
	//
	// The queued lane also caps how many cards it draws — a fifty-deep queue would otherwise
	// push every other lane off the screen, which is the scaling complaint the whole redesign
	// started from ("Board columns don't scale past a few tasks", chat 1).
	interface Props {
		stage: LifecycleStage;
		items: BoardItem[];
		collapsed: boolean;
		onToggle: () => void;
		onOpenCard: (item: BoardItem, anchor: HTMLElement) => void;
		/** Draw at most this many cards, with the rest behind a "+N more" control. */
		maxVisible?: number;
		emptyText?: string;
	}

	let { stage, items, collapsed, onToggle, onOpenCard, maxVisible, emptyText }: Props = $props();

	let expandedOverflow = $state(false);

	const hiddenCount = $derived(
		maxVisible && !expandedOverflow ? Math.max(0, items.length - maxVisible) : 0
	);
	const visible = $derived(hiddenCount > 0 ? items.slice(0, maxVisible) : items);
</script>

{#if collapsed}
	<button
		type="button"
		class="flex min-w-[52px] flex-col items-center gap-2.5 rounded-lg border border-slate-200 px-1.5 py-3 hover:bg-slate-50"
		onclick={onToggle}
		title="Expand this stage"
		data-testid="kanban-column-rail"
		data-stage={stage}
	>
		<svg
			width="14"
			height="14"
			viewBox="0 0 24 24"
			fill="none"
			stroke="currentColor"
			stroke-width="2.5"
			stroke-linecap="round"
			stroke-linejoin="round"
			class="text-slate-400"
			aria-hidden="true"
		>
			<path d="m9 6 6 6-6 6"></path>
			<path d="M4 4v16"></path>
		</svg>
		<span
			class="text-xs font-semibold tracking-wider text-slate-500 uppercase [writing-mode:vertical-rl]"
			>{LANE_LABELS[stage]}</span
		>
		<span class="text-sm font-medium text-slate-700" data-testid="kanban-column-count"
			>{items.length}</span
		>
	</button>
{:else}
	<section
		class="flex min-w-64 flex-1 flex-col gap-3 rounded-lg bg-slate-50 p-3"
		data-testid="kanban-column"
		data-stage={stage}
	>
		<header class="flex items-center justify-between">
			<div class="flex items-center gap-1.5">
				<button
					type="button"
					class="grid h-6 w-6 place-items-center rounded-full text-slate-400 hover:bg-slate-200 hover:text-slate-700"
					onclick={onToggle}
					title="Collapse this stage"
					aria-label="Collapse {LANE_LABELS[stage]}"
					data-testid="kanban-column-collapse"
				>
					<svg
						width="14"
						height="14"
						viewBox="0 0 24 24"
						fill="none"
						stroke="currentColor"
						stroke-width="2.5"
						stroke-linecap="round"
						stroke-linejoin="round"
						aria-hidden="true"
					>
						<path d="m15 6-6 6 6 6"></path>
						<path d="M20 4v16"></path>
					</svg>
				</button>
				<h2 class="text-xs font-semibold tracking-wider text-slate-600 uppercase">
					{LANE_LABELS[stage]}
				</h2>
			</div>
			<span class="text-xs text-slate-400" data-testid="kanban-column-count">{items.length}</span>
		</header>

		<div class="flex flex-col gap-2">
			{#each visible as item (item.key)}
				<BoardCard {item} onOpen={onOpenCard} />
			{/each}
		</div>

		{#if hiddenCount > 0}
			<button
				type="button"
				class="rounded border border-dashed border-slate-300 px-3 py-1 text-xs text-slate-500 hover:border-slate-400 hover:text-slate-700"
				onclick={() => (expandedOverflow = true)}
				data-testid="kanban-column-overflow">+{hiddenCount} more ▾</button
			>
		{:else if expandedOverflow && maxVisible && items.length > maxVisible}
			<button
				type="button"
				class="rounded border border-dashed border-slate-300 px-3 py-1 text-xs text-slate-500 hover:border-slate-400 hover:text-slate-700"
				onclick={() => (expandedOverflow = false)}
				data-testid="kanban-column-overflow">collapse ▴</button
			>
		{/if}

		{#if items.length === 0 && emptyText}
			<p class="px-1 py-2 text-xs text-slate-400" data-testid="kanban-column-empty">{emptyText}</p>
		{/if}
	</section>
{/if}
