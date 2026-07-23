import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import Page from './+page.svelte';

// T060 (US3): starting a new conversation (FR-010) regenerates conversationId and clears
// turns/activeTurnId; while a turn is running, the prompt form is visibly disabled and
// explained as "one turn at a time" (FR-008 UI half).

const { onAnswerChunkHandlers, onTurnChangedHandlers, startMock, stopMock, submitQueryTurnMock } = vi.hoisted(() => ({
	onAnswerChunkHandlers: [] as Array<(event: unknown) => void>,
	onTurnChangedHandlers: [] as Array<(event: unknown) => void>,
	startMock: vi.fn(),
	stopMock: vi.fn(),
	submitQueryTurnMock: vi.fn()
}));

vi.mock('$lib/services/queryLifecycleClient', () => ({
	createQueryLifecycleClient: () => ({
		start: async () => {
			startMock();
		},
		stop: async () => {
			stopMock();
		},
		onAnswerChunk: (handler: (event: unknown) => void) => {
			onAnswerChunkHandlers.push(handler);
			return () => {};
		},
		onTurnChanged: (handler: (event: unknown) => void) => {
			onTurnChangedHandlers.push(handler);
			return () => {};
		},
		onReconnected: () => () => {},
		onConnectionStateChanged: () => () => {}
	}),
	applyAnswerChunk: (currentAnswer: string, event: { text: string; sequence: number }, lastAppliedSequence: number) => {
		if (event.sequence <= lastAppliedSequence) return { answer: currentAnswer, lastAppliedSequence };
		return { answer: currentAnswer + event.text, lastAppliedSequence: event.sequence };
	},
	applyTurnChanged: (event: { eventId: string; turnId: string }, seen: Set<string>) => {
		const key = `${event.eventId}:${event.turnId}`;
		if (seen.has(key)) return false;
		seen.add(key);
		return true;
	}
}));

vi.mock('$lib/services/querySubmissionApi', () => ({
	submitQueryTurn: (...args: unknown[]) => submitQueryTurnMock(...args),
	interruptQueryTurn: vi.fn()
}));

test('submitting a question shows a running turn and disables the prompt form with the one-turn-at-a-time hint', async () => {
	onAnswerChunkHandlers.length = 0;
	onTurnChangedHandlers.length = 0;
	submitQueryTurnMock.mockReset();
	submitQueryTurnMock.mockResolvedValue({
		turnId: 't-1',
		conversationId: 'ignored-by-page-state',
		position: 1,
		state: 'running',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(Page);

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();

	await expect.element(screen.getByTestId('query-turn-prompt')).toHaveTextContent('What does ADR-004 decide?');
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');
	await expect.element(screen.getByTestId('query-prompt-input')).toBeDisabled();
	await expect.element(screen.getByTestId('query-prompt-disabled-hint')).toBeVisible();
});

test('starting a new conversation clears turns and re-enables the prompt form', async () => {
	onAnswerChunkHandlers.length = 0;
	onTurnChangedHandlers.length = 0;
	submitQueryTurnMock.mockReset();
	submitQueryTurnMock.mockResolvedValue({
		turnId: 't-2',
		conversationId: 'ignored-by-page-state',
		position: 1,
		state: 'running',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(Page);

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-prompt')).toBeVisible();

	await screen.getByTestId('query-new-conversation-button').click();

	await expect.element(screen.getByTestId('query-prompt-input')).not.toBeDisabled();
	expect(screen.container.querySelector('[data-testid="query-turn"]')).toBeNull();
});
