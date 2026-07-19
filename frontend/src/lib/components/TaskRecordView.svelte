<script lang="ts">
	import DOMPurify from 'dompurify';
	import { marked } from 'marked';
	import type { TaskRecord } from '$lib/types';

	interface Props {
		record: TaskRecord | null;
	}

	let { record }: Props = $props();

	// Sanitize even though the record is system/agent-authored: it embeds arbitrary
	// source-derived text (Principle V — the backend only parses/strips frontmatter, it
	// never templates or rewrites the record's content).
	const renderedBody = $derived(
		record ? DOMPurify.sanitize(marked.parse(record.body, { async: false }) as string) : ''
	);

	function formatTimestamp(value: string | null): string | null {
		if (!value) return null;
		const parsed = new Date(value);
		return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
	}
</script>

{#if record}
	<div class="flex flex-col gap-4" data-testid="task-record-view">
		<header
			class="flex flex-col gap-2 rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm"
			data-testid="task-record-metadata"
		>
			<div class="flex flex-wrap items-center gap-2">
				<span class="font-medium text-slate-900" data-testid="task-record-status"
					>{record.metadata.status}</span
				>
				{#if record.metadata.agent}
					<span class="text-slate-500" data-testid="task-record-agent">{record.metadata.agent}</span
					>
				{/if}
			</div>
			<dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-slate-600">
				<dt class="text-slate-400">Started</dt>
				<dd data-testid="task-record-started-at">{formatTimestamp(record.metadata.startedAt)}</dd>
				{#if record.metadata.completedAt}
					<dt class="text-slate-400">Completed</dt>
					<dd data-testid="task-record-completed-at">
						{formatTimestamp(record.metadata.completedAt)}
					</dd>
				{/if}
				{#if record.metadata.sourceRef}
					<dt class="text-slate-400">Source</dt>
					<dd class="truncate" data-testid="task-record-source-ref">{record.metadata.sourceRef}</dd>
				{/if}
				{#if record.metadata.originalRef}
					<dt class="text-slate-400">Original</dt>
					<dd class="truncate" data-testid="task-record-original-ref">
						{record.metadata.originalRef}
					</dd>
				{/if}
			</dl>
			{#if record.metadata.failureReason}
				<p class="text-stage-failed" data-testid="task-record-failure-reason">
					{record.metadata.failureReason}
				</p>
			{/if}
		</header>

		<div
			class="task-record-body max-h-[70vh] overflow-y-auto rounded-lg border border-slate-200 p-4 text-sm leading-relaxed break-words"
			data-testid="task-record-body"
		>
			<!-- record.body comes from the Hub's frontmatter-stripped markdown; marked renders it
			     and dompurify sanitizes the result above before this insertion. -->
			<!-- eslint-disable-next-line svelte/no-at-html-tags -->
			{@html renderedBody}
		</div>
	</div>
{:else}
	<div
		class="rounded-lg border border-dashed border-slate-300 p-6 text-center text-sm text-slate-500"
		data-testid="task-record-placeholder"
	>
		Task record unavailable.
	</div>
{/if}
