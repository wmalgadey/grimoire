import { expect, test } from 'vitest';
import { QuerySubmissionApiError, interruptQueryTurn, submitQueryTurn } from './querySubmissionApi';

// T088 (analyze finding, spec.md Edge Cases / Assumptions): a submission rejected over
// the concurrency limit or against an already-active conversation must show a clear,
// human-readable "busy" message — not the raw snake_case reason code the Hub returns.

function jsonResponse(status: number, body: unknown): Response {
	return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

test('submitQueryTurn maps a 503 concurrency-limit rejection to a human-readable message', async () => {
	const fetchImpl = async () => jsonResponse(503, { reason: 'query_concurrency_limit_reached' });

	const error = await submitQueryTurn('c-1', 'What does the wiki say?', [], fetchImpl).catch((e) => e);

	expect(error).toBeInstanceOf(QuerySubmissionApiError);
	expect((error as QuerySubmissionApiError).reason).toBe('query_concurrency_limit_reached');
	expect((error as QuerySubmissionApiError).message).toBe(
		'The wiki is busy right now — please try again in a moment.'
	);
});

test('submitQueryTurn maps a 409 conversation-already-active rejection to a human-readable message', async () => {
	const fetchImpl = async () => jsonResponse(409, { reason: 'conversation_already_active' });

	const error = await submitQueryTurn('c-1', 'What does the wiki say?', [], fetchImpl).catch((e) => e);

	expect(error).toBeInstanceOf(QuerySubmissionApiError);
	expect((error as QuerySubmissionApiError).reason).toBe('conversation_already_active');
	expect((error as QuerySubmissionApiError).message).toBe(
		'This conversation already has a question in progress.'
	);
});

test('an unrecognized reason code falls back to the raw code rather than throwing', async () => {
	const fetchImpl = async () => jsonResponse(400, { reason: 'some_future_reason_code' });

	const error = await submitQueryTurn('c-1', 'What does the wiki say?', [], fetchImpl).catch((e) => e);

	expect((error as QuerySubmissionApiError).message).toBe('some_future_reason_code');
});

test('interruptQueryTurn also maps rejection reason codes to human-readable messages', async () => {
	const fetchImpl = async () => jsonResponse(503, { reason: 'query_concurrency_limit_reached' });

	const error = await interruptQueryTurn('t-1', fetchImpl).catch((e) => e);

	expect((error as QuerySubmissionApiError).message).toBe(
		'The wiki is busy right now — please try again in a moment.'
	);
});
