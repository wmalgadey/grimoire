import { expect, test, vi } from 'vitest';
import {
	applyLintRunLifecycleEvent,
	createLintRunStream,
	fetchLintRunFromBoard,
	type LintLifecycleClient
} from './lintLifecycleClient';
import type { CompositeBoardResponse, LintRun, LintRunLifecycleEvent } from '$lib/types';

// T051 (015-lint-board-parity, PR #41 review convergence): regression coverage for the
// bug the review found — createLintRunStream().start() used to `await refresh()` (the
// /api/board bootstrap fetch) before `await client.start()` (the SignalR hub), so a
// transient board-fetch failure silently prevented the hub from ever connecting, even
// though the board page treats the bootstrap as best-effort and swallows start()'s
// rejection. FR-003/SC-001/SC-002 require live updates to keep working regardless.

function run(overrides: Partial<LintRun> = {}): LintRun {
	return {
		runId: 'run-1',
		status: 'running',
		triggeredAt: '2026-08-01T10:00:00Z',
		completedAt: null,
		failureReason: null,
		hasFindingsReport: false,
		...overrides
	};
}

function event(overrides: Partial<LintRunLifecycleEvent> = {}): LintRunLifecycleEvent {
	return {
		eventId: 'evt-1',
		runId: 'run-1',
		fromStatus: 'running',
		toStatus: 'completed',
		timestamp: '2026-08-01T10:05:00Z',
		failureReason: null,
		...overrides
	};
}

function boardResponse(entries: CompositeBoardResponse['entries'] = []): Response {
	return {
		ok: true,
		json: async () => ({ entries }) satisfies CompositeBoardResponse
	} as Response;
}

function createFakeClient() {
	let lifecycleHandler: ((event: LintRunLifecycleEvent) => void) | undefined;
	let reconnectedHandler: (() => void) | undefined;

	const client: LintLifecycleClient = {
		start: vi.fn().mockResolvedValue(undefined),
		stop: vi.fn().mockResolvedValue(undefined),
		onLintRunLifecycleChanged: vi.fn((handler) => {
			lifecycleHandler = handler;
			return () => {
				lifecycleHandler = undefined;
			};
		}),
		onReconnected: vi.fn((handler) => {
			reconnectedHandler = handler;
			return () => {
				reconnectedHandler = undefined;
			};
		}),
		onConnectionStateChanged: vi.fn(() => () => {})
	};

	return {
		client,
		emitLifecycleChanged: (evt: LintRunLifecycleEvent) => lifecycleHandler?.(evt),
		emitReconnected: () => reconnectedHandler?.()
	};
}

test('createLintRunStream starts the hub connection even when the /api/board bootstrap fetch fails', async () => {
	const fetchImpl = vi.fn().mockRejectedValue(new TypeError('network error'));
	const fake = createFakeClient();
	const onRunChanged = vi.fn();

	const stream = createLintRunStream(onRunChanged, { client: fake.client, fetchImpl });

	await stream.start();

	expect(fake.client.start).toHaveBeenCalledOnce();

	// The bootstrap failure means no board state was ever applied, but live events must
	// still take effect from here on (the hub connection is live).
	fake.emitLifecycleChanged(event({ toStatus: 'completed' }));
	expect(onRunChanged).toHaveBeenLastCalledWith(
		expect.objectContaining({ runId: 'run-1', status: 'completed' })
	);
});

test('createLintRunStream still rejects start() when the hub connection itself fails', async () => {
	const fetchImpl = vi.fn().mockResolvedValue(boardResponse());
	const fake = createFakeClient();
	fake.client.start = vi.fn().mockRejectedValue(new Error('hub unreachable'));

	const stream = createLintRunStream(vi.fn(), { client: fake.client, fetchImpl });

	await expect(stream.start()).rejects.toThrow('hub unreachable');
});

test('createLintRunStream bootstraps from the board API, then applies live events on top', async () => {
	const fetchImpl = vi
		.fn()
		.mockResolvedValue(boardResponse([{ kind: 'lint_run', ...run({ status: 'running' }) }]));
	const fake = createFakeClient();
	const onRunChanged = vi.fn();

	const stream = createLintRunStream(onRunChanged, { client: fake.client, fetchImpl });
	await stream.start();

	expect(onRunChanged).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'running' }));

	fake.emitLifecycleChanged(event({ toStatus: 'completed' }));
	expect(onRunChanged).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'completed' }));
});

test('applyLintRunLifecycleEvent applies an event exactly once per (eventId, runId)', () => {
	const seen = new Set<string>();
	const evt = event({ toStatus: 'completed' });

	const afterFirst = applyLintRunLifecycleEvent(run({ status: 'running' }), evt, seen);
	expect(afterFirst?.status).toBe('completed');

	const afterSecond = applyLintRunLifecycleEvent(afterFirst, evt, seen);
	expect(afterSecond).toBe(afterFirst);
});

test('applyLintRunLifecycleEvent ignores a stale "running" event after a terminal state', () => {
	const seen = new Set<string>();
	const terminal = run({ status: 'completed' });
	const staleRunningEvent = event({ eventId: 'evt-stale', toStatus: 'running' });

	const result = applyLintRunLifecycleEvent(terminal, staleRunningEvent, seen);

	expect(result).toBe(terminal);
});

// 023 T052: the Hub answers a rejected board read with a JSON reason; discarding it and
// showing only the status code turns an actionable message into "failed with status 409".
// 024 (ADR-026): the board read answers in the shared envelope. Discarding its sentence and
// showing only the status turned an actionable message into "failed with status 409".
test('fetchLintRunFromBoard surfaces the Hub sentence from the envelope', async () => {
	const fetchImpl = vi.fn().mockResolvedValue(
		new Response(
			JSON.stringify({
				status: 409,
				title: 'Declined',
				detail: 'This board read was declined for a stated reason.',
				code: 'board_read_declined'
			}),
			{ status: 409, headers: { 'Content-Type': 'application/problem+json' } }
		)
	);

	await expect(fetchLintRunFromBoard(fetchImpl as unknown as typeof fetch)).rejects.toThrow(
		'This board read was declined for a stated reason.'
	);
});

test('fetchLintRunFromBoard still reads as a sentence when the body is not an envelope', async () => {
	const fetchImpl = vi
		.fn()
		.mockResolvedValue(new Response('<html>Bad Gateway</html>', { status: 502 }));

	// Never "Request failed with status 502" — a status line tells a user nothing they can act on.
	await expect(fetchLintRunFromBoard(fetchImpl as unknown as typeof fetch)).rejects.toThrow(
		/wiki ran into a problem/
	);
});
