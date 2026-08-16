<script lang="ts">
	import {
		conversationNote,
		conversationTitle,
		type Conversation
	} from '$lib/stores/conversations.svelte';
	import { extractConversationCitations } from '$lib/wikiLinks';
	import { activeTurnId } from '$lib/stores/conversations.svelte';

	// The conversation overview the design added late ("if we have 'new conversation', we could
	// change the ask-button into a 'conversation overview'-page. this is currently missing",
	// chat 3): one card per conversation with its opening question, how far it got, whether it
	// is still answering, and the pages it touched.
	interface Props {
		conversations: Conversation[];
		onOpen: (id: string) => void;
	}

	let { conversations, onOpen }: Props = $props();
</script>

<div class="flex max-w-3xl flex-col gap-3" data-testid="conversation-list">
	{#each conversations as conversation (conversation.id)}
		{@const pages = extractConversationCitations(conversation.turns.map((t) => t.answer))}
		<button
			type="button"
			class="flex flex-col gap-2 rounded-lg border border-slate-200 bg-white p-4 text-left shadow-sm hover:border-blue-400 hover:shadow"
			onclick={() => onOpen(conversation.id)}
			data-testid="conversation-card"
			data-conversation-id={conversation.id}
		>
			<span class="flex flex-wrap items-center gap-2">
				<span class="text-sm font-medium text-slate-900" data-testid="conversation-card-title"
					>{conversationTitle(conversation)}</span
				>
				{#if activeTurnId(conversation)}
					<span
						class="inline-flex items-center rounded-full bg-blue-50 px-2 py-0.5 text-xs text-blue-700"
						data-testid="conversation-card-streaming">Answering…</span
					>
				{/if}
			</span>

			<span class="text-xs text-slate-500" data-testid="conversation-card-note"
				>{conversationNote(conversation)}</span
			>

			{#if pages.length > 0}
				<span class="flex flex-wrap gap-1.5">
					{#each pages as page (page.page)}
						<span
							class="inline-flex items-center rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-600"
							data-testid="conversation-card-page">{page.page}</span
						>
					{/each}
				</span>
			{/if}
		</button>
	{/each}
</div>
