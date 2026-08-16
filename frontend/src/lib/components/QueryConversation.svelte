<script lang="ts">
	import { presentRecordedFailure } from '$lib/services/apiError';
	import ApiErrorAlert from './ApiErrorAlert.svelte';
	import { renderMarkdown } from '$lib/markdown';
	import { linkifyCitations } from '$lib/wikiLinks';
	import type { QueryTurn } from '$lib/types';

	// The thread from the Hi-Fi design (5c): the question as a spoken block, the answer beneath
	// it as prose, and nothing else on the message — the Stop control moved into the composer
	// ("Stop now lives in the composer: while an answer streams, the Ask button becomes Stop",
	// chat 3), so a message carries only what was asked and what came back.
	//
	// The agent's `[[page]]` citations become inline Obsidian links ("the citation inside the
	// answer should be inline as links in the text", chat 3). Everything is still parsed by
	// marked and sanitized by DOMPurify — untrusted source content, Principle V.
	interface Props {
		turns: QueryTurn[];
	}

	let { turns }: Props = $props();

	const stateLabels: Record<QueryTurn['state'], string> = {
		running: 'Answering…',
		completed: 'Completed',
		interrupted: 'Interrupted',
		failed: 'Failed'
	};

	function renderAnswer(answer: string): string {
		return renderMarkdown(linkifyCitations(answer));
	}
</script>

<div class="flex flex-col gap-6" data-testid="query-conversation">
	{#each turns as turn (turn.turnId)}
		<article class="flex max-w-[74ch] flex-col gap-2" data-testid="query-turn">
			<p
				class="self-start rounded-2xl bg-slate-100 px-4 py-2 text-sm font-medium text-slate-900"
				data-testid="query-turn-prompt"
			>
				{turn.prompt}
			</p>

			<div data-testid="query-turn-answer" data-turn-state={turn.state}>
				{#if turn.state === 'running'}
					<!-- Streamed text is partial/unclosed markdown, so it is shown as plain text rather
					     than parsed — parsing incomplete markdown produces broken HTML. -->
					<p class="text-sm whitespace-pre-wrap text-slate-500">
						{turn.answer}<span
							class="ml-0.5 animate-pulse font-bold text-blue-500"
							data-testid="query-turn-streaming-cursor"
							aria-hidden="true">▌</span
						>
					</p>
				{:else}
					<div class="query-turn-answer-body text-sm text-slate-700">
						<!-- eslint-disable-next-line svelte/no-at-html-tags -->
						{@html renderAnswer(turn.answer)}
					</div>
				{/if}
			</div>

			{#if turn.state !== 'completed'}
				<!-- A completed turn says so by simply being there; the other three states are worth
				     a word (the design keeps only the "Answering…" tag on the message itself). -->
				<span
					class="inline-flex w-fit items-center rounded-full px-2 py-0.5 text-xs"
					class:bg-blue-50={turn.state === 'running'}
					class:text-blue-700={turn.state === 'running'}
					class:bg-amber-50={turn.state === 'interrupted'}
					class:text-amber-700={turn.state === 'interrupted'}
					class:bg-red-50={turn.state === 'failed'}
					class:text-red-700={turn.state === 'failed'}
					data-testid="query-turn-state"
				>
					{stateLabels[turn.state]}
				</span>
			{:else}
				<span class="sr-only" data-testid="query-turn-state">{stateLabels[turn.state]}</span>
			{/if}

			{#if turn.state === 'failed' && turn.failureReason}
				<ApiErrorAlert
					error={presentRecordedFailure(turn.failureReason)}
					testId="query-turn-failure-reason"
				/>
			{/if}
		</article>
	{/each}
</div>

<style>
	/* {@html}-injected markdown isn't part of Svelte's compiled markup, so its elements
	   never receive Svelte's scoping hash — :global() is required to reach them at all.
	   Needed because Tailwind Preflight zeroes margin/padding/list-style on every block
	   element project-wide, which otherwise collapses paragraphs/lists/headings in an
	   agent answer into an unreadable, spacing-free run. */
	.query-turn-answer-body :global(> :first-child) {
		margin-top: 0;
	}
	.query-turn-answer-body :global(> :last-child) {
		margin-bottom: 0;
	}
	.query-turn-answer-body :global(p),
	.query-turn-answer-body :global(ul),
	.query-turn-answer-body :global(ol),
	.query-turn-answer-body :global(blockquote),
	.query-turn-answer-body :global(pre) {
		margin-top: 0;
		margin-bottom: 0.75rem;
	}
	.query-turn-answer-body :global(h1),
	.query-turn-answer-body :global(h2),
	.query-turn-answer-body :global(h3),
	.query-turn-answer-body :global(h4),
	.query-turn-answer-body :global(h5),
	.query-turn-answer-body :global(h6) {
		margin-top: 1rem;
		margin-bottom: 0.5rem;
		font-weight: 600;
	}
	.query-turn-answer-body :global(ul) {
		list-style: disc;
		padding-left: 1.5rem;
	}
	.query-turn-answer-body :global(ol) {
		list-style: decimal;
		padding-left: 1.5rem;
	}
	.query-turn-answer-body :global(li) {
		margin-bottom: 0.25rem;
	}
	.query-turn-answer-body :global(blockquote) {
		padding-left: 0.75rem;
		border-left: 2px solid var(--color-slate-300);
	}
	.query-turn-answer-body :global(pre) {
		overflow-x: auto;
		border-radius: 0.375rem;
		background-color: var(--color-slate-100);
		padding: 0.5rem 0.75rem;
	}
	.query-turn-answer-body :global(code) {
		font-family: ui-monospace, monospace;
		font-size: 0.85em;
	}
	/* The inline citations the design asks for: a link in the running text, marked with the
	   same ↗ every outbound page link carries. */
	.query-turn-answer-body :global(a) {
		font-weight: 600;
		color: var(--color-slate-900);
		text-decoration: underline;
		text-underline-offset: 3px;
	}
</style>
