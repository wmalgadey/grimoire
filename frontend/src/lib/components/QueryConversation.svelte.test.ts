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

	await expect
		.element(screen.getByTestId('query-turn-prompt'))
		.toHaveTextContent('What does ADR-004 decide?');
	await expect.element(screen.getByTestId('query-turn-answer')).toHaveTextContent('ADR-004');
	await expect.element(screen.getByTestId('query-turn-state')).toHaveTextContent('Answering…');

	await screen.rerender({ turns: [turn({ answer: 'ADR-004 scopes the API key ' })] });
	await expect
		.element(screen.getByTestId('query-turn-answer'))
		.toHaveTextContent('ADR-004 scopes the API key');
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

// T105 (Convergence) - the answer is agent-authored markdown (bold, lists, [[wikilink]]
// citations); it must render as formatted HTML, not raw markup, and untrusted content
// must be sanitized (mirrors TaskRecordView.svelte's marked+DOMPurify rendering).
test('renders the answer as formatted markdown, not raw markup', async () => {
	const screen = await render(QueryConversation, {
		turns: [turn({ answer: '**ADR-004** scopes the key. See [[adr-004]].', state: 'completed' })]
	});

	const answerEl = screen.getByTestId('query-turn-answer').element();
	expect(answerEl.querySelector('strong')?.textContent).toBe('ADR-004');
	expect(answerEl.innerHTML).not.toContain('**ADR-004**');
});

// T109 (Convergence) - Tailwind Preflight zeroes margin/padding/list-style on every
// block element project-wide, so without scoped CSS the rendered answer's paragraphs
// and list items ran together with no blank-line separation and lists lost their
// bullets/indentation; a real user reported the rendering looked wrong.
test('applies visible spacing between paragraphs and list markers to the rendered answer', async () => {
	const screen = await render(QueryConversation, {
		turns: [
			turn({
				answer: 'First paragraph.\n\nSecond paragraph.\n\n- item one\n- item two',
				state: 'completed'
			})
		]
	});

	const answerEl = screen.getByTestId('query-turn-answer').element();
	const paragraphs = answerEl.querySelectorAll('p');
	expect(paragraphs.length).toBe(2);
	expect(parseFloat(getComputedStyle(paragraphs[0]).marginBottom)).toBeGreaterThan(0);

	const list = answerEl.querySelector('ul');
	expect(list).not.toBeNull();
	expect(getComputedStyle(list as Element).listStyleType).not.toBe('none');
});

test('sanitizes a script-injection payload in the answer text', async () => {
	const screen = await render(QueryConversation, {
		turns: [turn({ answer: '<img src=x onerror="alert(1)">safe text', state: 'completed' })]
	});

	const answerEl = screen.getByTestId('query-turn-answer').element();
	expect(answerEl.innerHTML).not.toContain('onerror');
	expect(answerEl.textContent).toContain('safe text');
});

// T106 (Convergence) - a real user missed the small "Answering…" label and mistook a
// still-streaming answer for a complete one; a cue attached to the answer text itself
// must make the in-progress state obvious, and disappear once the turn is terminal.
test('shows an in-progress streaming cue while running, absent once terminal', async () => {
	const screen = await render(QueryConversation, {
		turns: [turn({ answer: 'partial answer ', state: 'running' })]
	});

	await expect.element(screen.getByTestId('query-turn-streaming-cursor')).toBeVisible();

	await screen.rerender({ turns: [turn({ answer: 'partial answer done.', state: 'completed' })] });
	await expect.element(screen.getByTestId('query-turn-streaming-cursor')).not.toBeInTheDocument();
});

test('shows a stop control only while the turn is running, and calls onInterrupt', async () => {
	const onInterrupt = vi.fn();
	const screen = await render(QueryConversation, {
		turns: [turn({ state: 'running' })],
		onInterrupt
	});

	await expect.element(screen.getByTestId('query-turn-stop-button')).toBeVisible();
	await screen.getByTestId('query-turn-stop-button').click();
	expect(onInterrupt).toHaveBeenCalledWith('t-1');

	await screen.rerender({ turns: [turn({ state: 'completed' })], onInterrupt });
	await expect.element(screen.getByTestId('query-turn-stop-button')).not.toBeInTheDocument();
});
