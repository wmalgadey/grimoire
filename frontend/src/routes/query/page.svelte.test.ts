import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import { conversations } from '$lib/stores/conversations.svelte';

// T060 (US3) and the 011/024 convergence tests, carried onto the design's Ask surface: the
// tab lands on a conversation overview, "+ Ask" opens a fresh thread, and inside a thread the
// turn lifecycle behaves exactly as it did — one question at a time (FR-008), only the prompt
// on the wire (ADR-014/FR-009), reconnect reconciliation, pagehide interrupts, and a failed
// Stop presented rather than swallowed (024 FR-010/SC-005).

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

beforeEach(() => {
	// The conversation list outlives any one component, which is the point of it — so each
	// test starts from an empty one.
	conversations.reset();
	// The "+ Ask" flag is read off the real URL, and the page strips it — reset it so one
	// test's navigation intent cannot leak into the next.
	history.replaceState(history.state, '', window.location.pathname);
	onAnswerChunkHandlers.length = 0;
	onTurnChangedHandlers.length = 0;
	onReconnectedHandlers.length = 0;
	submitQueryTurnMock.mockReset();
	getQueryTurnMock.mockReset();
	interruptQueryTurnMock.mockReset();
	interruptQueryTurnMock.mockResolvedValue({});
});

function accepted(turnId: string, position = 1) {
	return {
		turnId,
		conversationId: 'ignored-by-page-state',
		position,
		state: 'running',
		acceptedAt: new Date().toISOString()
	};
}

/** Renders the page and opens a fresh thread, which is where every turn assertion lives. */
async function renderThread() {
	const screen = await render(Page);
	await screen.getByTestId('nav-ask-button').click();
	await expect.element(screen.getByTestId('query-prompt-input')).toBeVisible();
	return screen;
}

function completeTurn(turnId: string) {
	for (const handler of onTurnChangedHandlers) {
		handler({
			eventId: `e-${turnId}`,
			turnId,
			fromState: 'running',
			toState: 'completed',
			timestamp: new Date().toISOString(),
			failureReason: null
		});
	}
}

test('renders a nav link back to the board', async () => {
	const screen = await render(Page);

	const link = screen.getByTestId('nav-link-board');
	await expect.element(link).toBeVisible();
	await expect.element(link).toHaveAttribute('href', '/');
});

// The overview the design added: the tab lands on a list, not straight in a thread.
test('the tab lands on the conversation overview, empty until one is started', async () => {
	const screen = await render(Page);

	await expect.element(screen.getByTestId('conversation-list-empty')).toBeVisible();
	await expect.element(screen.getByTestId('query-prompt-input')).not.toBeInTheDocument();

	await screen.getByTestId('nav-ask-button').click();

	await expect.element(screen.getByTestId('thread-empty')).toBeVisible();
	await expect.element(screen.getByTestId('query-prompt-input')).toBeVisible();
});

test('arriving from another screen via "+ Ask" opens a fresh thread, not the overview', async () => {
	// The nav renders "+ Ask" as a plain link everywhere except this page, so the intent has
	// to survive the navigation — landing on the overview would leave the action one click
	// short of what its label promises.
	history.replaceState(history.state, '', `${window.location.pathname}?new=1`);

	const screen = await render(Page);

	await expect.element(screen.getByTestId('thread-empty')).toBeVisible();
	await expect.element(screen.getByTestId('query-prompt-input')).toBeVisible();
	await expect.element(screen.getByTestId('conversation-list-empty')).not.toBeInTheDocument();

	// Stripped again, so a refresh does not open a second empty conversation on top.
	expect(window.location.search).toBe('');
});

test('a conversation appears in the overview and reopens from it', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-list-1'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-prompt')).toBeVisible();

	await screen.getByTestId('back-to-conversations').click();

	await expect
		.element(screen.getByTestId('conversation-card-title'))
		.toHaveTextContent('What does ADR-004 decide?');
	await expect.element(screen.getByTestId('conversation-card-streaming')).toBeVisible();

	await screen.getByTestId('conversation-card').click();
	await expect.element(screen.getByTestId('query-turn-prompt')).toBeVisible();
});

// T107 (Convergence) / 011 T020 — the context promise is still disclosed, in the composer's
// permanent footnote.
test('discloses that follow-up questions carry the conversation context so far', async () => {
	const screen = await renderThread();

	await expect
		.element(screen.getByTestId('query-context-hint'))
		.toHaveTextContent(/everything asked and answered so far/i);
});

// T016 (011-query-conversations, FR-009/ADR-014): a follow-up sends only the prompt.
test('a follow-up submission sends only the conversationId and prompt, no priorTurns', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-follow-1'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	completeTurn('t-follow-1');
	await expect.element(screen.getByTestId('query-prompt-input')).not.toBeDisabled();

	submitQueryTurnMock.mockResolvedValue(accepted('t-follow-2', 2));
	await screen.getByTestId('query-prompt-input').fill('And how does that relate?');
	await screen.getByTestId('query-prompt-submit-button').click();

	expect(submitQueryTurnMock).toHaveBeenCalledTimes(2);
	for (const call of submitQueryTurnMock.mock.calls) {
		// Exactly (conversationId, prompt) — no third priorTurns argument.
		expect(call).toHaveLength(2);
		expect(typeof call[0]).toBe('string');
		expect(typeof call[1]).toBe('string');
	}
	// Both turns belong to the same conversation, which is what makes them follow-ups.
	expect(submitQueryTurnMock.mock.calls[0][0]).toBe(submitQueryTurnMock.mock.calls[1][0]);
	expect(submitQueryTurnMock.mock.calls[1][1]).toBe('And how does that relate?');
});

test('submitting a question shows a running turn and turns the composer into Stop', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-1'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();

	await expect
		.element(screen.getByTestId('query-turn-prompt'))
		.toHaveTextContent('What does ADR-004 decide?');
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');
	await expect.element(screen.getByTestId('query-prompt-input')).toBeDisabled();
	await expect.element(screen.getByTestId('query-prompt-stop-button')).toBeVisible();
});

test('starting a new conversation leaves the previous one intact and opens an empty thread', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-2'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-prompt')).toBeVisible();

	await screen.getByTestId('nav-ask-button').click();

	await expect.element(screen.getByTestId('query-prompt-input')).not.toBeDisabled();
	expect(screen.container.querySelector('[data-testid="query-turn"]')).toBeNull();

	// The earlier conversation is not lost — it is a row in the overview now.
	await screen.getByTestId('back-to-conversations').click();
	await expect.poll(() => conversations.list.length).toBe(2);
});

// The design's citation rail: the pages an answer cited, ready to open in Obsidian.
test('the rail lists the pages an answer cited, linked into Obsidian', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-cite'));
	getQueryTurnMock.mockResolvedValue({
		turnId: 't-cite',
		conversationId: 'c',
		position: 1,
		prompt: 'What is the retention window?',
		answer: 'Decided in [[policies/retention]], restated in [[ops/backups]].',
		state: 'completed',
		failureReason: null
	});
	const screen = await renderThread();

	await expect.element(screen.getByTestId('conversation-rail-empty')).toBeVisible();

	await screen.getByTestId('query-prompt-input').fill('What is the retention window?');
	await screen.getByTestId('query-prompt-submit-button').click();
	for (const handler of onReconnectedHandlers) handler();

	await expect
		.poll(() => screen.container.querySelectorAll('[data-testid="conversation-rail-page"]').length)
		.toBe(2);
	await expect
		.element(screen.getByTestId('conversation-rail-page').first())
		.toHaveAttribute('href', 'obsidian://open?file=policies%2Fretention');
});

// T086 (analyze finding, quickstart.md Scenario 6): reconnect reconciles the authoritative
// state via REST before the stream resumes.
test('reconnecting refreshes the running turn from the server and re-enables the composer', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-3'));
	getQueryTurnMock.mockResolvedValue({
		turnId: 't-3',
		conversationId: 'ignored-by-page-state',
		position: 1,
		prompt: 'What does ADR-004 decide?',
		answer: 'ADR-004 scopes the credential to the child process environment.',
		state: 'completed',
		failureReason: null
	});

	const screen = await renderThread();
	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	for (const handler of onReconnectedHandlers) handler();

	expect(getQueryTurnMock).toHaveBeenCalledWith('t-3');
	await expect
		.element(screen.getByTestId('query-turn-answer'))
		.toHaveTextContent('ADR-004 scopes the credential to the child process environment.');
	await expect.element(screen.getByTestId('query-prompt-input')).not.toBeDisabled();
});

// T089 (spec.md Edge Cases): an in-flight turn at reload time is treated as interrupted.
test('a pagehide event with an active turn calls interruptQueryTurn', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-4'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	window.dispatchEvent(new Event('pagehide'));

	expect(interruptQueryTurnMock).toHaveBeenCalledTimes(1);
	expect(interruptQueryTurnMock.mock.calls[0][0]).toBe('t-4');
});

test('a pagehide event with no active turn does not call interruptQueryTurn', async () => {
	await render(Page);

	window.dispatchEvent(new Event('pagehide'));

	expect(interruptQueryTurnMock).not.toHaveBeenCalled();
});

// With more than one conversation held at once, a turn streaming in a conversation that is
// not on screen still has to be interrupted when the page unloads.
test('a pagehide event interrupts a running turn in a conversation that is not on screen', async () => {
	submitQueryTurnMock.mockResolvedValue(accepted('t-background'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	await screen.getByTestId('nav-ask-button').click();
	window.dispatchEvent(new Event('pagehide'));

	expect(interruptQueryTurnMock.mock.calls[0][0]).toBe('t-background');
});

// 024 Phase 10 convergence (FR-010, FR-011, SC-005): a Stop that never reached the Hub
// produces no lifecycle event, so it must not be silent.
test('a failed interrupt is presented in the shared region rather than swallowed', async () => {
	interruptQueryTurnMock.mockRejectedValue(new TypeError('Failed to fetch'));
	submitQueryTurnMock.mockResolvedValue(accepted('t-5'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	await screen.getByTestId('query-prompt-stop-button').click();

	await expect.element(screen.getByTestId('query-interrupt-error')).toBeInTheDocument();
	await expect
		.element(screen.getByTestId('query-interrupt-error-message'))
		.toHaveTextContent('The wiki did not respond.');
});

// FR-008: retrying an interrupt that failed to reach the Hub can plausibly succeed.
test('retrying a failed interrupt re-runs it for the same turn', async () => {
	interruptQueryTurnMock.mockRejectedValue(new TypeError('Failed to fetch'));
	submitQueryTurnMock.mockResolvedValue(accepted('t-6'));
	const screen = await renderThread();

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	await screen.getByTestId('query-prompt-submit-button').click();
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	await screen.getByTestId('query-prompt-stop-button').click();
	await expect.element(screen.getByTestId('query-interrupt-error')).toBeInTheDocument();

	interruptQueryTurnMock.mockResolvedValue({});
	await screen.getByTestId('query-interrupt-error-retry').click();

	await vi.waitFor(() =>
		expect(screen.container.querySelector('[data-testid="query-interrupt-error"]')).toBeNull()
	);
	expect(interruptQueryTurnMock).toHaveBeenCalledTimes(2);
	expect(interruptQueryTurnMock.mock.calls[1][0]).toBe('t-6');
});
