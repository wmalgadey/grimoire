import { expect, test, vi } from 'vitest';
import {
	applyRemediationTaskLifecycleEvent,
	createRemediationTaskStream,
	type RemediationLifecycleClient
} from './remediationLifecycleClient';
import type {
	CompositeBoardResponse,
	RemediationTaskBoardEntry,
	RemediationTaskLifecycleEvent
} from '$lib/types';

// T052 (015-lint-board-parity, PR #41 review convergence): mirrors T051's
// lintLifecycleClient regression coverage for the identical bug — createRemediationTaskStream().start()
// used to `await refresh()` before `await client.start()`, so a transient /api/board
// failure silently prevented the remediation hub from ever connecting. FR-003/SC-002
// require live updates to keep working regardless.

function taskEntry(overrides: Partial<RemediationTaskBoardEntry> = {}): RemediationTaskBoardEntry {
	return {
		kind: 'remediation_task',
		taskId: 'task-1',
		runId: 'run-1',
		title: 'Fix missing tag',
		state: 'proposed',
		proposedAt: '2026-08-01T10:00:00Z',
		queuePosition: null,
		outcomeReason: null,
		updatedAt: '2026-08-01T10:00:00Z',
		...overrides
	};
}

function event(
	overrides: Partial<RemediationTaskLifecycleEvent> = {}
): RemediationTaskLifecycleEvent {
	return {
		eventId: 'evt-1',
		taskId: 'task-1',
		runId: 'run-1',
		fromState: 'proposed',
		toState: 'authorized',
		timestamp: '2026-08-01T10:05:00Z',
		queuePosition: 1,
		outcomeReason: null,
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
	let lifecycleHandler: ((event: RemediationTaskLifecycleEvent) => void) | undefined;
	let reconnectedHandler: (() => void) | undefined;

	const client: RemediationLifecycleClient = {
		start: vi.fn().mockResolvedValue(undefined),
		stop: vi.fn().mockResolvedValue(undefined),
		onRemediationTaskLifecycleChanged: vi.fn((handler) => {
			lifecycleHandler = handler;
			return () => {
				lifecycleHandler = undefined;
			};
		}),
		onRemediationMessageTurnChanged: vi.fn(
			() => () => {}
		) as RemediationLifecycleClient['onRemediationMessageTurnChanged'],
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
		emitLifecycleChanged: (evt: RemediationTaskLifecycleEvent) => lifecycleHandler?.(evt),
		emitReconnected: () => reconnectedHandler?.()
	};
}

test('createRemediationTaskStream starts the hub connection even when the /api/board bootstrap fetch fails', async () => {
	const fetchImpl = vi.fn().mockRejectedValue(new TypeError('network error'));
	const fake = createFakeClient();
	const onTasksChanged = vi.fn();

	const stream = createRemediationTaskStream(onTasksChanged, { client: fake.client, fetchImpl });

	await stream.start();

	expect(fake.client.start).toHaveBeenCalledOnce();
});

test('createRemediationTaskStream still rejects start() when the hub connection itself fails', async () => {
	const fetchImpl = vi.fn().mockResolvedValue(boardResponse());
	const fake = createFakeClient();
	fake.client.start = vi.fn().mockRejectedValue(new Error('hub unreachable'));

	const stream = createRemediationTaskStream(vi.fn(), { client: fake.client, fetchImpl });

	await expect(stream.start()).rejects.toThrow('hub unreachable');
});

test('createRemediationTaskStream bootstraps from the board API, then applies live events on top', async () => {
	const fetchImpl = vi.fn().mockResolvedValue(boardResponse([taskEntry({ state: 'proposed' })]));
	const fake = createFakeClient();
	const onTasksChanged = vi.fn();

	const stream = createRemediationTaskStream(onTasksChanged, { client: fake.client, fetchImpl });
	await stream.start();

	expect(onTasksChanged).toHaveBeenLastCalledWith([expect.objectContaining({ state: 'proposed' })]);

	fake.emitLifecycleChanged(event({ toState: 'authorized' }));
	expect(onTasksChanged).toHaveBeenLastCalledWith([
		expect.objectContaining({ taskId: 'task-1', state: 'authorized' })
	]);
});

test('applyRemediationTaskLifecycleEvent applies an event exactly once per (eventId, taskId)', () => {
	const seen = new Set<string>();
	const entries = [taskEntry({ state: 'proposed' })];
	const evt = event({ toState: 'authorized' });

	const afterFirst = applyRemediationTaskLifecycleEvent(entries, evt, seen);
	expect(afterFirst.entries[0].state).toBe('authorized');
	expect(afterFirst.unknownTask).toBe(false);

	const afterSecond = applyRemediationTaskLifecycleEvent(afterFirst.entries, evt, seen);
	expect(afterSecond.entries).toBe(afterFirst.entries);
});

test('applyRemediationTaskLifecycleEvent signals unknownTask for a task the client has not bootstrapped yet', () => {
	const seen = new Set<string>();

	const result = applyRemediationTaskLifecycleEvent([], event({ taskId: 'task-new' }), seen);

	expect(result.unknownTask).toBe(true);
	expect(result.entries).toEqual([]);
});
