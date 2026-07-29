import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import Page from './+page.svelte';

// T060 (US3): starting a new conversation (FR-010) regenerates conversationId and clears
// turns/activeTurnId; while a turn is running, the prompt form is visibly disabled and
// explained as "one turn at a time" (FR-008 UI half).

const {
	onAnswerChunkHandlers,
	onTurnChangedHandlers,
	onReconnectedHandlers,
	startMock,
	stopMock,
	submitQueryTurnMock,
	getQueryTurnMock,
	interruptQueryTurnMock
} = vi.hoisted(() => ({
	onAnswerChunkHandlers: [] as Array<(event: unknown) => void>,
	onTurnChangedHandlers: [] as Array<(event: unknown) => void>,
	onReconnectedHandlers: [] as Array<() => void>,
	startMock: vi.fn(),
	stopMock: vi.fn(),
	submitQueryTurnMock: vi.fn(),
	getQueryTurnMock: vi.fn(),
	interruptQueryTurnMock: vi.fn()
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
		onReconnected: (handler: () => void) => {
			onReconnectedHandlers.push(handler);
			return () => {};
		},
		onConnectionStateChanged: () => () => {}
	}),
	applyAnswerChunk: (
		currentAnswer: string,
		event: { text: string; sequence: number },
		lastAppliedSequence: number
	) => {
		if (event.sequence <= lastAppliedSequence)
			return { answer: currentAnswer, lastAppliedSequence };
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
	interruptQueryTurn: (...args: unknown[]) => interruptQueryTurnMock(...args),
	getQueryTurn: (...args: unknown[]) => getQueryTurnMock(...args)
}));

// T103 (Convergence) - the query surface had no way back to the ingest UI either;
// asserts the nav link back to root exists.
test('renders a nav link back to the ingest UI', async () => {
	const screen = await render(Page);

	const link = screen.getByTestId('nav-link-ingest');
	await expect.element(link).toBeVisible();
	await expect.element(link).toHaveAttribute('href', '/');
});

// T107 (Convergence) / 011 T020 - the context hint stays accurate under
// record-sourced context (ADR-014): follow-up questions still see the whole
// conversation so far, only the Hub now assembles it from the Conversation Record.
test('discloses that follow-up questions carry the conversation context so far', async () => {
	const screen = await render(Page);

	await expect
		.element(screen.getByTestId('query-context-hint'))
		.toHaveTextContent(/everything asked and answered so far/i);
});

// T016 (011-query-conversations, FR-009/ADR-014): a follow-up submission sends only
// the prompt — the page no longer assembles a priorTurns payload from its on-screen
// conversation state.
test('a follow-up submission sends only the conversationId and prompt, no priorTurns', async () => {
	onAnswerChunkHandlers.length = 0;
	onTurnChangedHandlers.length = 0;
	submitQueryTurnMock.mockReset();
	submitQueryTurnMock.mockResolvedValue({
		turnId: 't-follow-1',
		conversationId: 'ignored-by-page-state',
		position: 1,
		state: 'running',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(Page);

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	// Complete the first turn so a follow-up becomes possible.
	for (const handler of onTurnChangedHandlers) {
		handler({
			eventId: 'e-follow-1',
			turnId: 't-follow-1',
			fromState: 'running',
			toState: 'completed',
			timestamp: new Date().toISOString(),
			failureReason: null
		});
	}
	await expect.element(screen.getByTestId('query-prompt-input')).not.toBeDisabled();

	submitQueryTurnMock.mockResolvedValue({
		turnId: 't-follow-2',
		conversationId: 'ignored-by-page-state',
		position: 2,
		state: 'running',
		acceptedAt: new Date().toISOString()
	});

	await screen.getByTestId('query-prompt-input').fill('And how does that relate?');
	await screen.getByTestId('query-prompt-submit-button').click();

	expect(submitQueryTurnMock).toHaveBeenCalledTimes(2);
	for (const call of submitQueryTurnMock.mock.calls) {
		// Exactly (conversationId, prompt) — no third priorTurns argument.
		expect(call).toHaveLength(2);
		expect(typeof call[0]).toBe('string');
		expect(typeof call[1]).toBe('string');
	}
	expect(submitQueryTurnMock.mock.calls[1][1]).toBe('And how does that relate?');
});

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

	await expect
		.element(screen.getByTestId('query-turn-prompt'))
		.toHaveTextContent('What does ADR-004 decide?');
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

// T086 (analyze finding, quickstart.md Scenario 6): on reconnect, the active turn's
// authoritative state is refreshed via GET /api/query-turns/{turnId} before resuming the
// stream (contracts/query-conversation-api.md ## Rules) — reconciles any answer/state
// missed while disconnected, and re-enables the prompt form once the refreshed state is
// terminal.
test('reconnecting refreshes the active turn from the server and re-enables the form once terminal', async () => {
	onAnswerChunkHandlers.length = 0;
	onTurnChangedHandlers.length = 0;
	onReconnectedHandlers.length = 0;
	submitQueryTurnMock.mockReset();
	getQueryTurnMock.mockReset();
	submitQueryTurnMock.mockResolvedValue({
		turnId: 't-3',
		conversationId: 'ignored-by-page-state',
		position: 1,
		state: 'running',
		acceptedAt: new Date().toISOString()
	});
	getQueryTurnMock.mockResolvedValue({
		turnId: 't-3',
		conversationId: 'ignored-by-page-state',
		position: 1,
		prompt: 'What does ADR-004 decide?',
		answer: 'ADR-004 scopes the credential to the child process environment.',
		state: 'completed',
		failureReason: null
	});

	const screen = await render(Page);

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	for (const handler of onReconnectedHandlers) handler();

	expect(getQueryTurnMock).toHaveBeenCalledWith('t-3');
	await expect
		.element(screen.getByTestId('query-turn-answer'))
		.toHaveTextContent('ADR-004 scopes the credential to the child process environment.');
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Completed');
	await expect.element(screen.getByTestId('query-prompt-input')).not.toBeDisabled();
});

// T089 (analyze finding, spec.md Edge Cases / Assumptions): an in-flight turn at reload
// time is treated as interrupted — a `pagehide` event (reload/navigation/tab-close) with
// an active turn calls the interrupt endpoint before the page unloads.
test('a pagehide event with an active turn calls interruptQueryTurn', async () => {
	onAnswerChunkHandlers.length = 0;
	onTurnChangedHandlers.length = 0;
	submitQueryTurnMock.mockReset();
	interruptQueryTurnMock.mockReset();
	interruptQueryTurnMock.mockResolvedValue({});
	submitQueryTurnMock.mockResolvedValue({
		turnId: 't-4',
		conversationId: 'ignored-by-page-state',
		position: 1,
		state: 'running',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(Page);

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	window.dispatchEvent(new Event('pagehide'));

	expect(interruptQueryTurnMock).toHaveBeenCalledTimes(1);
	expect(interruptQueryTurnMock.mock.calls[0][0]).toBe('t-4');
});

test('a pagehide event with no active turn does not call interruptQueryTurn', async () => {
	onAnswerChunkHandlers.length = 0;
	onTurnChangedHandlers.length = 0;
	interruptQueryTurnMock.mockReset();

	await render(Page);

	window.dispatchEvent(new Event('pagehide'));

	expect(interruptQueryTurnMock).not.toHaveBeenCalled();
});
