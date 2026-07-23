import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import QueryConversation from './QueryConversation.svelte';
import type { QueryTurn } from '$lib/types';

// T030 (US1): renders progressively-arriving answer text as queryAnswerChunk events
// apply (simulated here via re-render with an updated `turns` prop, since the actual
// SignalR event application is a pure function tested independently in
// queryLifecycleClient.test.ts — this component is presentational), and displays page
// references (the agent's own citation wikilinks) once the turn completes.

function turn(overrides: Partial<QueryTurn> = {}): QueryTurn {
	return {
		turnId: 't-1',
		conversationId: 'c-1',
		position: 1,
		prompt: 'What does ADR-004 decide?',
		answer: '',
		state: 'running',
		...overrides
	};
}

test('renders the prompt and progressively-arriving answer text as it grows', async () => {
	const screen = await render(QueryConversation, { turns: [turn({ answer: 'ADR-004 ' })] });

	await expect.element(screen.getByTestId('query-turn-prompt')).toHaveTextContent(
		'What does ADR-004 decide?'
	);
	await expect.element(screen.getByTestId('query-turn-answer')).toHaveTextContent('ADR-004');
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	await screen.rerender({ turns: [turn({ answer: 'ADR-004 scopes the API key ' })] });
	await expect.element(screen.getByTestId('query-turn-answer')).toHaveTextContent(
		'ADR-004 scopes the API key'
	);
});

test('displays the full answer with page-reference wikilinks once the turn completes', async () => {
	const completedAnswer = 'ADR-004 scopes the credential to [[adr-004]] and [[adr-009]].';
	const screen = await render(QueryConversation, {
		turns: [turn({ answer: completedAnswer, state: 'completed' })]
	});

	await expect.element(screen.getByTestId('query-turn-answer')).toHaveTextContent(completedAnswer);
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Completed');
});

test('shows the failure reason when a turn fails', async () => {
	const screen = await render(QueryConversation, {
		turns: [turn({ state: 'failed', failureReason: 'Query agent process crashed.' })]
	});

	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Failed');
	await expect
		.element(screen.getByTestId('query-turn-failure-reason'))
		.toHaveTextContent('Query agent process crashed.');
});

test('shows a stop control only while the turn is running, and calls onInterrupt', async () => {
	const onInterrupt = vi.fn();
	const screen = await render(QueryConversation, { turns: [turn({ state: 'running' })], onInterrupt });

	await expect.element(screen.getByTestId('query-turn-stop-button')).toBeVisible();
	await screen.getByTestId('query-turn-stop-button').click();
	expect(onInterrupt).toHaveBeenCalledWith('t-1');

	await screen.rerender({ turns: [turn({ state: 'completed' })], onInterrupt });
	await expect.element(screen.getByTestId('query-turn-stop-button')).not.toBeInTheDocument();
});
