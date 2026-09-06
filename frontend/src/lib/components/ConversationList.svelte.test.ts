import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import ConversationList from './ConversationList.svelte';
import type { Conversation } from '$lib/stores/conversations.svelte';
import type { QueryTurn } from '$lib/types';

// The conversation overview: opening question, how far it got, whether it is still answering,
// and the pages it touched — all read off the turns the browser already holds, nothing
// invented (the citation counts come from the agent's own `[[page]]` convention).

function turn(overrides: Partial<QueryTurn> = {}): QueryTurn {
	return {
		turnId: 't-1',
		conversationId: 'c-1',
		position: 1,
		prompt: 'What did we decide about the retention window?',
		answer: 'Sources are kept for 90 days. Decided in [[policies/retention]].',
		state: 'completed',
		...overrides
	};
}

function conversation(overrides: Partial<Conversation> = {}): Conversation {
	return { id: 'c-1', turns: [turn()], ...overrides };
}

test('a conversation is headed by its opening question and counts its questions', async () => {
	const screen = await render(ConversationList, {
		conversations: [conversation({ turns: [turn(), turn({ turnId: 't-2', position: 2 })] })],
		onOpen: () => {}
	});

	await expect
		.element(screen.getByTestId('conversation-card-title'))
		.toHaveTextContent('What did we decide about the retention window?');
	await expect
		.element(screen.getByTestId('conversation-card-note'))
		.toHaveTextContent('2 questions');
});

test('an empty conversation says so rather than showing a blank heading', async () => {
	const screen = await render(ConversationList, {
		conversations: [conversation({ turns: [] })],
		onOpen: () => {}
	});

	await expect
		.element(screen.getByTestId('conversation-card-title'))
		.toHaveTextContent('New conversation');
	await expect
		.element(screen.getByTestId('conversation-card-note'))
		.toHaveTextContent('no questions yet');
});

test('a conversation with a streaming answer is marked as answering', async () => {
	const screen = await render(ConversationList, {
		conversations: [conversation({ turns: [turn({ state: 'running', answer: 'partial' })] })],
		onOpen: () => {}
	});

	await expect.element(screen.getByTestId('conversation-card-streaming')).toBeVisible();
	await expect
		.element(screen.getByTestId('conversation-card-note'))
		.toHaveTextContent('answering now');
});

test('the pages the conversation cited are listed on its card', async () => {
	const screen = await render(ConversationList, {
		conversations: [
			conversation({
				turns: [turn({ answer: 'See [[policies/retention]] and [[ops/backups]].' })]
			})
		],
		onOpen: () => {}
	});

	const pages = screen.container.querySelectorAll('[data-testid="conversation-card-page"]');
	expect([...pages].map((p) => p.textContent)).toEqual(['policies/retention', 'ops/backups']);
});

test('clicking a card opens that conversation', async () => {
	const onOpen = vi.fn();
	const screen = await render(ConversationList, {
		conversations: [conversation({ id: 'c-42' })],
		onOpen
	});

	await screen.getByTestId('conversation-card').click();

	expect(onOpen).toHaveBeenCalledWith('c-42');
});
