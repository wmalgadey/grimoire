import { expect, test, vi } from 'vitest';
import {
	applyLifecycleEvent,
	createBoardLifecycleStream,
	createIngestLifecycleClient,
	type IngestLifecycleClient
} from './ingestLifecycleClient';
import type {
	BoardTask,
	ConnectionState,
	LifecycleEvent,
	TaskRecordChangedEvent
} from '$lib/types';

// T074 (US2, convergence): exercises the rules mandated by
// contracts/ingest-lifecycle-events.md ## Rules that the component-level KanbanColumn test
// (T041) never drives through the actual client: idempotent event application by
// (eventId, taskId), stale/out-of-order rejection, live event application on top of the
// bootstrapped board, and the reconnect-then-refresh flow.

// T054 (004 US4, FR-023/SC-012): exercises createIngestLifecycleClient's connection-state
// projection against a fake HubConnection double (no real network) — connecting before
// start() settles, connected once it resolves or reconnects, reconnecting on the
// connection's own onreconnecting callback, disconnected on start() rejection or onclose.

const { createFakeConnection, getLastFakeConnection } = vi.hoisted(() => {
	let last: ReturnType<typeof build> | undefined;

	function build() {
		return {
			start: vi.fn(),
			stop: vi.fn(),
			on: vi.fn(),
			off: vi.fn(),
			onreconnecting: vi.fn(),
			onreconnected: vi.fn(),
			onclose: vi.fn()
		};
	}

	function createFakeConnection() {
		last = build();
		return last;
	}

	function getLastFakeConnection() {
		if (!last) throw new Error('no fake connection created yet');
		return last;
	}

	return { createFakeConnection, getLastFakeConnection };
});

vi.mock('@microsoft/signalr', () => ({
	HubConnectionBuilder: vi.fn().mockImplementation(function FakeHubConnectionBuilder(this: {
		withUrl: () => unknown;
		withAutomaticReconnect: () => unknown;
		build: () => unknown;
	}) {
		this.withUrl = () => this;
		this.withAutomaticReconnect = () => this;
		this.build = () => createFakeConnection();
	})
}));

vi.mock('$lib/services/ingestSubmissionsApi', async () => {
	const actual = await vi.importActual<typeof import('$lib/services/ingestSubmissionsApi')>(
		'$lib/services/ingestSubmissionsApi'
	);
	return {
		...actual,
		listBoard: vi.fn()
	};
});

function task(overrides: Partial<BoardTask>): BoardTask {
	return {
		taskId: 'task-1',
		status: 'received',
		title: 'task-1',
		updatedAt: '2026-07-08T10:00:00Z',
		failureReason: null,
		taskLink: '/api/ingest-submissions/task-1',
		...overrides
	};
}

function event(overrides: Partial<LifecycleEvent>): LifecycleEvent {
	return {
		eventId: 'evt-1',
		taskId: 'task-1',
		fromStatus: 'received',
		toStatus: 'converting',
		timestamp: '2026-07-08T10:01:00Z',
		failureReason: null,
		...overrides
	};
}

function createFakeClient() {
	let lifecycleHandler: ((event: LifecycleEvent) => void) | undefined;
	let reconnectedHandler: (() => void) | undefined;

	const client: IngestLifecycleClient = {
		start: vi.fn().mockResolvedValue(undefined),
		stop: vi.fn().mockResolvedValue(undefined),
		onLifecycleChanged: vi.fn((handler) => {
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
		onRunActivityChanged: vi.fn(() => () => {}),
		onTaskRecordChanged: vi.fn(() => () => {}),
		onConnectionStateChanged: vi.fn(() => () => {})
	};

	return {
		client,
		emitLifecycleChanged: (evt: LifecycleEvent) => lifecycleHandler?.(evt),
		emitReconnected: () => reconnectedHandler?.()
	};
}

test('applyLifecycleEvent applies an event exactly once per (eventId, taskId)', () => {
	const seen = new Set<string>();
	const tasks = [task({ status: 'received', updatedAt: '2026-07-08T10:00:00Z' })];
	const evt = event({ toStatus: 'converting', timestamp: '2026-07-08T10:01:00Z' });

	const afterFirst = applyLifecycleEvent(tasks, evt, seen);
	expect(afterFirst[0].status).toBe('converting');

	const afterSecond = applyLifecycleEvent(afterFirst, evt, seen);
	expect(afterSecond).toBe(afterFirst);
	expect(afterSecond[0].status).toBe('converting');
});

test('applyLifecycleEvent ignores a stale/out-of-order event older than the task state it holds', () => {
	const seen = new Set<string>();
	const tasks = [task({ status: 'queued', updatedAt: '2026-07-08T12:00:00Z' })];
	const staleEvent = event({
		eventId: 'evt-stale',
		toStatus: 'converting',
		timestamp: '2026-07-08T10:01:00Z'
	});

	const result = applyLifecycleEvent(tasks, staleEvent, seen);

	expect(result).toBe(tasks);
	expect(result[0].status).toBe('queued');
});

test('applyLifecycleEvent adds a new task for an event whose taskId is not yet on the board', () => {
	const seen = new Set<string>();
	const evt = event({ taskId: 'task-new', fromStatus: null, toStatus: 'received' });

	const result = applyLifecycleEvent([], evt, seen);

	expect(result).toHaveLength(1);
	expect(result[0].taskId).toBe('task-new');
	expect(result[0].status).toBe('received');
});

test('createBoardLifecycleStream bootstraps from the board API, then applies live events on top', async () => {
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.listBoard).mockResolvedValue([
		task({ taskId: 'task-1', status: 'received', updatedAt: '2026-07-08T10:00:00Z' })
	]);

	const fake = createFakeClient();
	const onTasksChanged = vi.fn();
	const stream = createBoardLifecycleStream(onTasksChanged, { client: fake.client });

	await stream.start();
	expect(onTasksChanged).toHaveBeenLastCalledWith([
		task({ taskId: 'task-1', status: 'received', updatedAt: '2026-07-08T10:00:00Z' })
	]);
	expect(fake.client.start).toHaveBeenCalledOnce();

	fake.emitLifecycleChanged(
		event({ taskId: 'task-1', toStatus: 'converting', timestamp: '2026-07-08T10:01:00Z' })
	);

	expect(onTasksChanged).toHaveBeenLastCalledWith([
		expect.objectContaining({ taskId: 'task-1', status: 'converting' })
	]);
});

test('createBoardLifecycleStream refreshes the board from the REST API on reconnect', async () => {
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.listBoard)
		.mockReset()
		.mockResolvedValueOnce([task({ taskId: 'task-1', status: 'converting' })])
		.mockResolvedValueOnce([task({ taskId: 'task-1', status: 'queued' })]);

	const fake = createFakeClient();
	const onTasksChanged = vi.fn();
	const stream = createBoardLifecycleStream(onTasksChanged, { client: fake.client });

	await stream.start();
	expect(api.listBoard).toHaveBeenCalledTimes(1);

	fake.emitReconnected();
	await vi.waitFor(() => expect(api.listBoard).toHaveBeenCalledTimes(2));

	expect(onTasksChanged).toHaveBeenLastCalledWith([
		expect.objectContaining({ taskId: 'task-1', status: 'queued' })
	]);
});

// T032/T037 (US3, contracts/task-record-changed-event.md): onTaskRecordChanged delivers
// events from the taskRecordChanged channel and dedupes by eventId, consistent with the
// applyLifecycleEvent idempotency rule for taskLifecycleChanged.

function taskRecordChangedEvent(
	overrides: Partial<TaskRecordChangedEvent> = {}
): TaskRecordChangedEvent {
	return {
		eventId: 'evt-1',
		taskId: 'task-1',
		changedAt: '2026-07-19T10:00:00Z',
		...overrides
	};
}

function getRegisteredHandler(
	connection: ReturnType<typeof getLastFakeConnection>,
	channel: string
): (event: TaskRecordChangedEvent) => void {
	const call = connection.on.mock.calls.find(([name]) => name === channel);
	if (!call) throw new Error(`no handler registered for "${channel}"`);
	return call[1] as (event: TaskRecordChangedEvent) => void;
}

test('onTaskRecordChanged delivers events from the taskRecordChanged channel', () => {
	const client = createIngestLifecycleClient();
	const connection = getLastFakeConnection();

	const received: TaskRecordChangedEvent[] = [];
	client.onTaskRecordChanged((event) => received.push(event));

	const handler = getRegisteredHandler(connection, 'taskRecordChanged');
	handler(taskRecordChangedEvent());

	expect(received).toHaveLength(1);
	expect(received[0]).toEqual(taskRecordChangedEvent());
});

test('onTaskRecordChanged dedupes by eventId', () => {
	const client = createIngestLifecycleClient();
	const connection = getLastFakeConnection();

	const received: TaskRecordChangedEvent[] = [];
	client.onTaskRecordChanged((event) => received.push(event));

	const handler = getRegisteredHandler(connection, 'taskRecordChanged');
	const evt = taskRecordChangedEvent();
	handler(evt);
	handler(evt);

	expect(received).toHaveLength(1);
});

test('onConnectionStateChanged reflects connecting, connected, reconnecting, and back to connected', async () => {
	const client = createIngestLifecycleClient();
	const connection = getLastFakeConnection();
	connection.start.mockResolvedValue(undefined);

	const states: ConnectionState[] = [];
	client.onConnectionStateChanged((state) => states.push(state));

	const startPromise = client.start();
	expect(states).toEqual(['connecting']);

	await startPromise;
	expect(states).toEqual(['connecting', 'connected']);

	const reconnecting = connection.onreconnecting.mock.calls[0][0] as () => void;
	reconnecting();
	expect(states).toEqual(['connecting', 'connected', 'reconnecting']);

	const reconnected = connection.onreconnected.mock.calls[0][0] as () => void;
	reconnected();
	expect(states).toEqual(['connecting', 'connected', 'reconnecting', 'connected']);
});

test('onConnectionStateChanged emits disconnected on onclose', () => {
	const client = createIngestLifecycleClient();
	const connection = getLastFakeConnection();

	const states: ConnectionState[] = [];
	client.onConnectionStateChanged((state) => states.push(state));

	const closed = connection.onclose.mock.calls[0][0] as () => void;
	closed();

	expect(states).toEqual(['disconnected']);
});

test('onConnectionStateChanged emits disconnected when start() rejects', async () => {
	const client = createIngestLifecycleClient();
	const connection = getLastFakeConnection();
	connection.start.mockRejectedValue(new Error('boom'));

	const states: ConnectionState[] = [];
	client.onConnectionStateChanged((state) => states.push(state));

	await expect(client.start()).rejects.toThrow('boom');
	expect(states).toEqual(['connecting', 'disconnected']);
});
