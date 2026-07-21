import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import type { ConnectionState, TaskRecord, TaskRecordChangedEvent } from '$lib/types';

// T029 (US2): the detail route reads taskId from route params, fetches via
// getTaskRecord, and renders TaskRecordView — including the placeholder path for an
// unavailable record (404).
// T032/T038 (US3, contracts/task-record-changed-event.md): live updates — refetch on a
// taskRecordChanged event for this route's own taskId (and only its own), refetch
// unconditionally on reconnect (FR-010), and surface connection staleness via the
// existing ConnectionState/ConnectionStatusIndicator.

const { getTaskRecordMock } = vi.hoisted(() => ({
	getTaskRecordMock: vi.fn()
}));

vi.mock('$lib/services/ingestSubmissionsApi', () => ({
	getTaskRecord: getTaskRecordMock
}));

interface FakeLifecycleClient {
	start: () => Promise<void>;
	stop: () => Promise<void>;
	onLifecycleChanged: () => () => void;
	onRunActivityChanged: () => () => void;
	onTaskRecordChanged: (handler: (event: TaskRecordChangedEvent) => void) => () => void;
	onReconnected: (handler: () => void) => () => void;
	onConnectionStateChanged: (handler: (state: ConnectionState) => void) => () => void;
	emitTaskRecordChanged: (event: TaskRecordChangedEvent) => void;
	emitReconnected: () => void;
	emitConnectionStateChanged: (state: ConnectionState) => void;
}

const { createFakeLifecycleClient, getLastFakeLifecycleClient } = vi.hoisted(() => {
	let last: FakeLifecycleClient | undefined;

	function build(): FakeLifecycleClient {
		let taskRecordChangedHandler: ((event: TaskRecordChangedEvent) => void) | undefined;
		let reconnectedHandler: (() => void) | undefined;
		let connectionStateHandler: ((state: ConnectionState) => void) | undefined;

		return {
			start: () => Promise.resolve(),
			stop: () => Promise.resolve(),
			onLifecycleChanged: () => () => {},
			onRunActivityChanged: () => () => {},
			onTaskRecordChanged: (handler: (event: TaskRecordChangedEvent) => void) => {
				taskRecordChangedHandler = handler;
				return () => {
					taskRecordChangedHandler = undefined;
				};
			},
			onReconnected: (handler: () => void) => {
				reconnectedHandler = handler;
				return () => {
					reconnectedHandler = undefined;
				};
			},
			onConnectionStateChanged: (handler: (state: ConnectionState) => void) => {
				connectionStateHandler = handler;
				return () => {
					connectionStateHandler = undefined;
				};
			},
			emitTaskRecordChanged: (event: TaskRecordChangedEvent) => taskRecordChangedHandler?.(event),
			emitReconnected: () => reconnectedHandler?.(),
			emitConnectionStateChanged: (state: ConnectionState) => connectionStateHandler?.(state)
		};
	}

	function createFakeLifecycleClient() {
		last = build();
		return last;
	}

	function getLastFakeLifecycleClient() {
		if (!last) throw new Error('no fake lifecycle client created yet');
		return last;
	}

	return { createFakeLifecycleClient, getLastFakeLifecycleClient };
});

vi.mock('$lib/services/ingestLifecycleClient', () => ({
	createIngestLifecycleClient: () => createFakeLifecycleClient()
}));

function record(overrides: Partial<TaskRecord> = {}): TaskRecord {
	return {
		taskId: 'task-1',
		metadata: {
			status: 'completed',
			agent: 'ingest',
			startedAt: '2026-07-18T14:03:11.0000000Z',
			completedAt: '2026-07-18T14:05:00.0000000Z',
			sourceRef: 'raw/sources/task-1.md',
			originalRef: null,
			failureReason: null
		},
		body: '# Heading\n\nBody text.',
		...overrides
	};
}

test('reads taskId from route params and renders the fetched record', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });

	await expect.element(screen.getByTestId('task-record-page-title')).toHaveTextContent('task-1');
	await expect.element(screen.getByTestId('task-record-view')).toBeVisible();
	expect(getTaskRecordMock).toHaveBeenCalledWith('task-1');
});

test('renders the placeholder when the record is unavailable', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'unavailable' });

	const screen = await render(Page, {
		data: { taskId: 'missing-task' },
		params: { taskId: 'missing-task' }
	});

	await expect.element(screen.getByTestId('task-record-placeholder')).toBeVisible();
	await expect.element(screen.getByTestId('task-record-view')).not.toBeInTheDocument();
});

test('refetches on a taskRecordChanged event for its own taskId', async () => {
	getTaskRecordMock.mockReset();
	getTaskRecordMock
		.mockResolvedValueOnce({ status: 'ok', record: record({ body: 'first' }) })
		.mockResolvedValueOnce({ status: 'ok', record: record({ body: 'second' }) });

	await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await vi.waitFor(() => expect(getTaskRecordMock).toHaveBeenCalledTimes(1));

	const client = getLastFakeLifecycleClient();
	client.emitTaskRecordChanged({
		eventId: 'evt-1',
		taskId: 'task-1',
		changedAt: '2026-07-19T10:00:00Z'
	});

	await vi.waitFor(() => expect(getTaskRecordMock).toHaveBeenCalledTimes(2));
});

test('ignores a taskRecordChanged event for a different taskId', async () => {
	getTaskRecordMock.mockReset();
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await vi.waitFor(() => expect(getTaskRecordMock).toHaveBeenCalledTimes(1));

	const client = getLastFakeLifecycleClient();
	client.emitTaskRecordChanged({
		eventId: 'evt-1',
		taskId: 'some-other-task',
		changedAt: '2026-07-19T10:00:00Z'
	});

	// No new fetch: give any (incorrect) refetch a chance to happen before asserting it didn't.
	await new Promise((resolve) => setTimeout(resolve, 50));
	expect(getTaskRecordMock).toHaveBeenCalledTimes(1);
});

test('refetches unconditionally on reconnect (FR-010)', async () => {
	getTaskRecordMock.mockReset();
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await vi.waitFor(() => expect(getTaskRecordMock).toHaveBeenCalledTimes(1));

	const client = getLastFakeLifecycleClient();
	client.emitReconnected();

	await vi.waitFor(() => expect(getTaskRecordMock).toHaveBeenCalledTimes(2));
});

test('surfaces connection staleness via the shared ConnectionStatusIndicator', async () => {
	getTaskRecordMock.mockReset();
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	const screen = await render(Page, {
		data: { taskId: 'task-1' },
		params: { taskId: 'task-1' }
	});

	const client = getLastFakeLifecycleClient();
	client.emitConnectionStateChanged('disconnected');

	await expect
		.element(screen.getByTestId('connection-status-indicator'))
		.toHaveAttribute('data-connection-state', 'disconnected');
});
