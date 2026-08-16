import { expect, test } from 'vitest';
import {
	PresentedApiError,
	presentRecordedFailure,
	presentRequestFailure,
	presentResponseError,
	presentUnusableResponse,
	toPresentedError
} from './apiError';

/**
 * 024-api-error-presentation (T033, T043, T052). State-based throughout: an observed failure goes
 * in, a `PresentedError` comes out. Nothing here asserts that `fetch` rejects or that a `Response`
 * parses — those are the platform's behaviours, not ours (Constitution Principle II).
 */

function problem(status: number, body: Record<string, unknown>): Response {
	return new Response(JSON.stringify(body), {
		status,
		headers: { 'Content-Type': 'application/problem+json' }
	});
}

// ---------------------------------------------------------------------------
// T043 — the derivation table (data-model.md), one case per row
// ---------------------------------------------------------------------------

test('a request that never produced a response is unreachable, and retryable', () => {
	const presented = presentRequestFailure();

	expect(presented.category).toBe('unreachable');
	expect(presented.retryable).toBe(true);
	expect(presented.status).toBeNull();
	expect(presented.message.length).toBeGreaterThan(20);
});

test('a 4xx carrying the envelope is declined, and not retryable', async () => {
	const presented = await presentResponseError(
		problem(409, {
			status: 409,
			title: 'Conversation is busy',
			detail: 'This conversation is still working on the previous question.',
			code: 'conversation_already_active',
			traceId: 'trace-abc'
		})
	);

	expect(presented.category).toBe('declined');
	expect(presented.retryable).toBe(false);
	expect(presented.title).toBe('Conversation is busy');
	expect(presented.message).toBe('This conversation is still working on the previous question.');
	expect(presented.code).toBe('conversation_already_active');
	expect(presented.traceId).toBe('trace-abc');
	expect(presented.bodyExcerpt).toBeNull();
});

test('a 4xx whose body is not an envelope is unexpected, and not retryable', async () => {
	const presented = await presentResponseError(
		new Response('<html>not our shape</html>', { status: 404 })
	);

	expect(presented.category).toBe('unexpected');
	expect(presented.retryable).toBe(false);
	expect(presented.bodyExcerpt).toContain('not our shape');
});

test('a 5xx carrying the envelope is a fault, and retryable', async () => {
	const presented = await presentResponseError(
		problem(500, {
			status: 500,
			title: 'Something went wrong',
			detail: 'The request could not be completed because of an internal error.',
			code: 'internal_error'
		})
	);

	expect(presented.category).toBe('fault');
	expect(presented.retryable).toBe(true);
	expect(presented.traceId).toBeNull();
});

/**
 * The case that drove the classification rule. A gateway answering 502 with an HTML page is the
 * commonest real infrastructure failure and is maximally worth retrying; a body-driven rule would
 * file it as "unexpected" and withhold the retry, which is exactly backwards.
 */
test('a 5xx whose body is not an envelope is still a fault, and still retryable', async () => {
	const presented = await presentResponseError(
		new Response('<html>502 Bad Gateway</html>', { status: 502 })
	);

	expect(presented.category).toBe('fault');
	expect(presented.retryable).toBe(true);
	expect(presented.bodyExcerpt).toContain('Bad Gateway');
	expect(presented.message).not.toContain('<html>');
});

test('a successful response with an unusable payload is unexpected', () => {
	const presented = presentUnusableResponse(200, 'null');

	expect(presented.category).toBe('unexpected');
	expect(presented.retryable).toBe(false);
	expect(presented.status).toBe(200);
});

test('a body claiming problem+json but carrying neither detail nor code is not treated as an envelope', async () => {
	const presented = await presentResponseError(problem(400, { something: 'else' }));

	expect(presented.category).toBe('unexpected');
	expect(presented.code).toBeNull();
});

// ---------------------------------------------------------------------------
// SC-002 / FR-013 — every category has something readable to show
// ---------------------------------------------------------------------------

test('no category can produce an empty message, a status line, or an identifier', async () => {
	const presented = [
		presentRequestFailure(),
		await presentResponseError(new Response('', { status: 404 })),
		await presentResponseError(new Response('', { status: 500 })),
		presentUnusableResponse(200)
	];

	for (const p of presented) {
		expect(p.message.trim().length).toBeGreaterThan(20);
		expect(p.message).not.toMatch(/^Request failed with status/);
		expect(p.message).not.toMatch(/^[a-z_]+$/);
		expect(p.title.trim().length).toBeGreaterThan(0);
	}
});

// ---------------------------------------------------------------------------
// T033 — length bounding (FR-014, SC-007)
// ---------------------------------------------------------------------------

test('an over-length detail is shortened for reading and kept in full for the details', async () => {
	const long = `The provider rejected this request. ${'x'.repeat(400)}`;
	const presented = await presentResponseError(
		problem(400, { status: 400, title: 'Declined', detail: long, code: 'some_code' })
	);

	expect(presented.message.length).toBeLessThan(long.length);
	expect(presented.message.endsWith('…')).toBe(true);
	expect(presented.fullMessage).toBe(long);
});

test('a message short enough to read whole carries no duplicate full copy', async () => {
	const presented = await presentResponseError(
		problem(400, { status: 400, title: 'Declined', detail: 'Short enough.', code: 'c' })
	);

	expect(presented.message).toBe('Short enough.');
	expect(presented.fullMessage).toBeNull();
});

// ---------------------------------------------------------------------------
// T052 — recorded agent-run failures (FR-012)
// ---------------------------------------------------------------------------

test('a recorded provider rejection reads as the provider sentence, with our framing demoted', () => {
	const presented = presentRecordedFailure(
		'Model API error 400 (invalid_request_error): prompt is too long: 235583 tokens > 200000 maximum'
	);

	expect(presented.category).toBe('fault');
	expect(presented.message).toBe('prompt is too long: 235583 tokens > 200000 maximum');
	expect(presented.bodyExcerpt).toBe('Model API error 400 (invalid_request_error)');
});

test('a recorded failure without our prefix is presented untouched', () => {
	const recorded = 'Ingest failed: Agent run showed no liveness for 60 seconds and was terminated.';

	expect(presentRecordedFailure(recorded).message).toBe(recorded);
});

test('redacted content in a recorded failure survives presentation unchanged', () => {
	const presented = presentRecordedFailure(
		'Model API error 401 (authentication_error): invalid key [REDACTED]'
	);

	expect(presented.message).toContain('[REDACTED]');
	expect(presented.message).not.toContain('sk-ant');
});

// ---------------------------------------------------------------------------
// toPresentedError — what surfaces actually call
// ---------------------------------------------------------------------------

test('a client error carries through the presentation its response produced', async () => {
	const presented = await presentResponseError(
		problem(409, { status: 409, title: 'Busy', detail: 'Try later.', code: 'busy' })
	);

	expect(toPresentedError({ presented })).toBe(presented);
});

test('an error carrying a status is never presented as unreachable', () => {
	// Claiming the wiki is unreachable when it plainly answered would send the user to check
	// their connection over a problem that has nothing to do with it.
	const presented = toPresentedError({ status: 409, message: 'Already running.' });

	expect(presented.category).toBe('declined');
	expect(presented.message).toBe('Already running.');
});

test('a thrown PresentedApiError round-trips its whole presentation', async () => {
	// Throwing a bare Error(message) used to lose the category here, so a 409 the wiki plainly
	// answered came back as "unreachable" — telling the user to check their connection.
	const original = await presentResponseError(
		problem(409, { status: 409, title: 'Busy', detail: 'Try later.', code: 'busy' })
	);

	const roundTripped = toPresentedError(new PresentedApiError(original));

	expect(roundTripped).toBe(original);
	expect(roundTripped.category).toBe('declined');
	expect(roundTripped.code).toBe('busy');
});

test('anything else reaching a catch block means the request never completed', () => {
	expect(toPresentedError(new TypeError('Failed to fetch')).category).toBe('unreachable');
	expect(toPresentedError(undefined).category).toBe('unreachable');
});
