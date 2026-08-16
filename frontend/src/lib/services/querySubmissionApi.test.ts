import { expect, test } from 'vitest';
import { QuerySubmissionApiError, interruptQueryTurn, submitQueryTurn } from './querySubmissionApi';

// T088 (analyze finding, spec.md Edge Cases / Assumptions): a submission rejected over
// the concurrency limit or against an already-active conversation must show a clear,
// human-readable "busy" message — not the raw snake_case reason code the Hub returns.

/** 024 (ADR-026): the Hub answers every failure as application/problem+json. */
function problemResponse(status: number, code: string, detail: string): Response {
	return new Response(JSON.stringify({ status, title: 'Declined', detail, code }), {
		status,
		headers: { 'Content-Type': 'application/problem+json' }
	});
}

function jsonResponse(status: number, body: unknown): Response {
	return new Response(JSON.stringify(body), {
		status,
		headers: { 'Content-Type': 'application/json' }
	});
}

// T016 (011-query-conversations, FR-009/ADR-014): the submission request body contains
// exactly one key — `prompt`. The browser no longer assembles or sends `priorTurns`;
// the Hub sources follow-up context from its Conversation Record.
test('submitQueryTurn sends a body containing exactly the prompt and no priorTurns key', async () => {
	let capturedBody: string | undefined;
	const fetchImpl = async (_input: RequestInfo | URL, init?: RequestInit) => {
		capturedBody = init?.body as string;
		return jsonResponse(202, {
			turnId: 't-1',
			conversationId: 'c-1',
			position: 1,
			state: 'running',
			acceptedAt: new Date().toISOString()
		});
	};

	await submitQueryTurn('c-1', 'What does the wiki say?', fetchImpl as typeof fetch);

	expect(capturedBody).toBeDefined();
	const parsed = JSON.parse(capturedBody!);
	expect(parsed).toEqual({ prompt: 'What does the wiki say?' });
	expect(Object.keys(parsed)).toEqual(['prompt']);
});

test('submitQueryTurn surfaces the Hub sentence for a 503 and keeps the code', async () => {
	const fetchImpl = async () =>
		problemResponse(
			503,
			'query_concurrency_limit_reached',
			'The wiki is answering as many questions as it can handle right now. Wait a moment and ask again.'
		);

	const error = await submitQueryTurn('c-1', 'What does the wiki say?', fetchImpl).catch((e) => e);

	expect(error).toBeInstanceOf(QuerySubmissionApiError);
	expect((error as QuerySubmissionApiError).reason).toBe('query_concurrency_limit_reached');
	// A 5xx is a fault, and a fault is retryable — the user has nothing to fix.
	expect((error as QuerySubmissionApiError).presented?.category).toBe('fault');
	expect((error as QuerySubmissionApiError).presented?.retryable).toBe(true);
	expect((error as QuerySubmissionApiError).presented?.message).toContain('Wait a moment');
});

test('submitQueryTurn presents a 409 as declined, with no retry offered', async () => {
	const fetchImpl = async () =>
		problemResponse(
			409,
			'conversation_already_active',
			'This conversation is still working on the previous question. Wait for the answer, then ask again.'
		);

	const error = await submitQueryTurn('c-1', 'What does the wiki say?', fetchImpl).catch((e) => e);

	expect((error as QuerySubmissionApiError).reason).toBe('conversation_already_active');
	expect((error as QuerySubmissionApiError).presented?.category).toBe('declined');
	// Retrying a declined request cannot help until the user resolves what caused it (FR-008).
	expect((error as QuerySubmissionApiError).presented?.retryable).toBe(false);
	expect((error as QuerySubmissionApiError).presented?.message).not.toContain(
		'conversation_already_active'
	);
});

test('a code this client has never heard of still reads as a sentence', async () => {
	// Previously this displayed the raw identifier, because the browser held the only
	// code→prose table and could not have an entry for a code it did not know about.
	const fetchImpl = async () =>
		problemResponse(400, 'some_future_code', 'Something specific the Hub wanted to say.');

	const error = await submitQueryTurn('c-1', 'What does the wiki say?', fetchImpl).catch((e) => e);

	expect((error as QuerySubmissionApiError).presented?.message).toBe(
		'Something specific the Hub wanted to say.'
	);
});

test('interruptQueryTurn presents rejections the same way', async () => {
	const fetchImpl = async () =>
		problemResponse(503, 'query_concurrency_limit_reached', 'Wait a moment and try again.');

	const error = await interruptQueryTurn('t-1', fetchImpl).catch((e) => e);

	expect((error as QuerySubmissionApiError).presented?.message).toBe(
		'Wait a moment and try again.'
	);
});
