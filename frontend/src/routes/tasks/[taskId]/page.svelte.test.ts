import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import type {
	ConnectionState,
	LifecycleEvent,
	TaskDetail,
	TaskRecord,
	TaskRecordChangedEvent
} from '$lib/types';

// T029 (US2): the detail route reads taskId from route params, fetches via
// getTaskRecord, and renders TaskRecordView — including the placeholder path for an
// unavailable record (404).
// T032/T038 (US3, contracts/task-record-changed-event.md): live updates — refetch on a
// taskRecordChanged event for this route's own taskId (and only its own), refetch
// unconditionally on reconnect (FR-010), and surface connection staleness via the
// existing ConnectionState/ConnectionStatusIndicator.
// 023 T011 (US1, FR-006/SC-004): the ordered status history renders on the detail page and
// is re-fetched whenever a taskLifecycleChanged event arrives for this route's own task.

const { getTaskRecordMock, getTaskDetailMock, restartTaskMock, FakeIngestSubmissionApiError } =
	vi.hoisted(() => {
		class FakeIngestSubmissionApiError extends Error {
			status: number;
			constructor(message: string, status: number) {
				super(message);
				this.status = status;
			}
		}

		return {
			getTaskRecordMock: vi.fn(),
			getTaskDetailMock: vi.fn(),
			restartTaskMock: vi.fn(),
			FakeIngestSubmissionApiError
		};
	});

vi.mock('$lib/services/ingestSubmissionsApi', () => ({
	getTaskRecord: getTaskRecordMock,
	getTaskDetail: getTaskDetailMock,
	restartTask: restartTaskMock,
	IngestSubmissionApiError: FakeIngestSubmissionApiError
}));

interface FakeLifecycleClient {
	start: () => Promise<void>;
	stop: () => Promise<void>;
	onLifecycleChanged: (handler: (event: LifecycleEvent) => void) => () => void;
	onRunActivityChanged: () => () => void;
	onTaskRecordChanged: (handler: (event: TaskRecordChangedEvent) => void) => () => void;
	onReconnected: (handler: () => void) => () => void;
	onConnectionStateChanged: (handler: (state: ConnectionState) => void) => () => void;
	emitLifecycleChanged: (event: LifecycleEvent) => void;
	emitTaskRecordChanged: (event: TaskRecordChangedEvent) => void;
	emitReconnected: () => void;
	emitConnectionStateChanged: (state: ConnectionState) => void;
}

const { createFakeLifecycleClient, getLastFakeLifecycleClient } = vi.hoisted(() => {
	let last: FakeLifecycleClient | undefined;

	function build(): FakeLifecycleClient {
		let lifecycleChangedHandler: ((event: LifecycleEvent) => void) | undefined;
		let taskRecordChangedHandler: ((event: TaskRecordChangedEvent) => void) | undefined;
		let reconnectedHandler: (() => void) | undefined;
		let connectionStateHandler: ((state: ConnectionState) => void) | undefined;

		return {
			start: () => Promise.resolve(),
			stop: () => Promise.resolve(),
			onLifecycleChanged: (handler: (event: LifecycleEvent) => void) => {
				lifecycleChangedHandler = handler;
				return () => {
					lifecycleChangedHandler = undefined;
				};
			},
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
			emitLifecycleChanged: (event: LifecycleEvent) => lifecycleChangedHandler?.(event),
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

function detail(overrides: Partial<TaskDetail> = {}): TaskDetail {
	return {
		taskId: 'task-1',
		status: 'completed',
		title: 'Getting Started',
		failureReason: null,
		sourceRef: 'raw/sources/task-1.md',
		originalRef: null,
		statusHistory: [
			{ status: 'received', enteredAt: '2026-08-13T07:00:01Z', detail: null },
			{ status: 'completed', enteredAt: '2026-08-13T07:03:40Z', detail: null }
		],
		...overrides
	};
}

beforeEach(() => {
	getTaskDetailMock.mockReset();
	getTaskDetailMock.mockResolvedValue(detail());
	restartTaskMock.mockReset();
	restartTaskMock.mockResolvedValue({ taskId: 'task-1', status: 'queued' });
});

test('reads taskId from route params and renders the fetched record', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });

	await expect.element(screen.getByTestId('task-record-view')).toBeVisible();
	expect(getTaskRecordMock).toHaveBeenCalledWith('task-1');
});

// ── 023 T022 (US3, FR-003/FR-004, SC-003) ─────────────────────────────────────────

test('heads the page with the human-readable label, keeping the raw id beneath it', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });

	await expect
		.element(screen.getByTestId('task-record-page-title'))
		.toHaveTextContent('Getting Started');
	await expect.element(screen.getByTestId('task-detail-task-id')).toHaveTextContent('task-1');
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

// ── 023 T011 (US1, FR-006/SC-004) ──────────────────────────────────────────────────

test('renders the ordered status history for the task', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });

	await expect.element(screen.getByTestId('status-history-path')).toBeVisible();
	await vi.waitFor(() => {
		const entries = screen.getByTestId('status-history-entry').elements();
		expect(entries.map((el) => el.getAttribute('data-status'))).toEqual(['received', 'completed']);
	});
	expect(getTaskDetailMock).toHaveBeenCalledWith('task-1');
});

test('re-fetches the history on a taskLifecycleChanged event for its own task', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await vi.waitFor(() => expect(getTaskDetailMock).toHaveBeenCalledTimes(1));

	const client = getLastFakeLifecycleClient();
	client.emitLifecycleChanged({
		eventId: 'evt-1',
		taskId: 'task-1',
		fromStatus: 'running',
		toStatus: 'completed',
		timestamp: '2026-08-13T07:03:40Z',
		failureReason: null
	});

	await vi.waitFor(() => expect(getTaskDetailMock).toHaveBeenCalledTimes(2));
});

test('ignores a taskLifecycleChanged event for a different task', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await vi.waitFor(() => expect(getTaskDetailMock).toHaveBeenCalledTimes(1));

	const client = getLastFakeLifecycleClient();
	client.emitLifecycleChanged({
		eventId: 'evt-2',
		taskId: 'some-other-task',
		fromStatus: 'queued',
		toStatus: 'running',
		timestamp: '2026-08-13T07:03:40Z',
		failureReason: null
	});

	// Give an (incorrect) refetch a chance to happen before asserting it didn't.
	await new Promise((resolve) => setTimeout(resolve, 50));
	expect(getTaskDetailMock).toHaveBeenCalledTimes(1);
});

test('renders the history panel with no entries when the task recorded none', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValue(detail({ statusHistory: [], status: 'failed' }));

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });

	await expect.element(screen.getByTestId('status-history-path')).toBeVisible();
	expect(screen.getByTestId('status-history-entry').elements()).toHaveLength(0);
});

// ── 023 T033 (US5, FR-010..FR-012, SC-007) ─────────────────────────────────────────

test('shows a Restart button only when the task is failed', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValue(detail({ status: 'failed' }));

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });

	await expect.element(screen.getByTestId('task-restart-button')).toBeVisible();
});

test('shows no Restart button for a non-failed task', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValue(detail({ status: 'completed' }));

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await vi.waitFor(() => expect(getTaskDetailMock).toHaveBeenCalled());

	await expect.element(screen.getByTestId('task-restart-button')).not.toBeInTheDocument();
});

test('clicking Restart calls the API, disables the button while in flight, and refetches after', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValue(detail({ status: 'failed' }));
	let resolveRestart: (() => void) | undefined;
	restartTaskMock.mockReturnValue(
		new Promise((resolve) => {
			resolveRestart = () => resolve({ taskId: 'task-1', status: 'queued' });
		})
	);

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	const button = screen.getByTestId('task-restart-button');
	await button.click();

	await expect.element(button).toBeDisabled();
	expect(restartTaskMock).toHaveBeenCalledWith('task-1');

	getTaskDetailMock.mockResolvedValue(detail({ status: 'queued' }));
	resolveRestart?.();

	await vi.waitFor(() => expect(getTaskDetailMock).toHaveBeenCalledTimes(2));
});

test('a 409 conflict on restart shows the reason and re-fetches the true state', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValue(detail({ status: 'failed' }));
	restartTaskMock.mockRejectedValue(
		new FakeIngestSubmissionApiError('Task is already restarting.', 409)
	);

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await screen.getByTestId('task-restart-button').click();

	await expect
		.element(screen.getByTestId('task-restart-error'))
		.toHaveTextContent('Task is already restarting.');
	// 024 SC-005 (FSI2): the shared presentation, with its technical detail one step away.
	await expect.element(screen.getByTestId('task-restart-error-details-toggle')).toBeInTheDocument();
	await vi.waitFor(() => expect(getTaskDetailMock).toHaveBeenCalledTimes(2));
});

// 024 FR-010/SC-005: the 409 is not the only way restart fails. An unreachable Hub used to be
// re-thrown out of the handler, leaving the page silent while TaskCard.svelte's identical
// button presented it — the one surface that disagreed with "the same treatment everywhere".
test('an unreachable Hub on restart is presented, not swallowed', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValue(detail({ status: 'failed' }));
	restartTaskMock.mockRejectedValue(new TypeError('Failed to fetch'));

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await screen.getByTestId('task-restart-button').click();

	await expect.element(screen.getByTestId('task-restart-error')).toBeInTheDocument();
	await expect
		.element(screen.getByTestId('task-restart-error-message'))
		.toHaveTextContent('The wiki did not respond.');
});

// The offline case as it actually happens: the restart AND the refresh that follows it both
// fail, because the Hub is unreachable for both. `detail` gates the error region, so a refresh
// failure that blanked it took the restart error down with it — the user clicked Restart and
// saw nothing at all. Only the Hub saying the task is gone may clear the detail.
test('a restart error survives the background refresh that fails alongside it', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValueOnce(detail({ status: 'failed' }));
	restartTaskMock.mockRejectedValue(new TypeError('Failed to fetch'));
	getTaskDetailMock.mockRejectedValue(new TypeError('Failed to fetch'));

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await screen.getByTestId('task-restart-button').click();

	await vi.waitFor(() => expect(getTaskDetailMock).toHaveBeenCalledTimes(2));
	await expect.element(screen.getByTestId('task-restart-error')).toBeInTheDocument();
});

// The counterpart: when the Hub does say the task is gone, the stale detail must not linger.
test('a 404 from the background refresh clears the detail', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValueOnce(detail({ status: 'failed' }));

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await expect.element(screen.getByTestId('task-restart-button')).toBeInTheDocument();

	getTaskDetailMock.mockRejectedValue(new FakeIngestSubmissionApiError('gone', 404));
	getLastFakeLifecycleClient().emitLifecycleChanged({
		eventId: 'evt-gone',
		taskId: 'task-1',
		fromStatus: 'failed',
		toStatus: 'failed',
		timestamp: '2026-08-16T07:03:40Z',
		failureReason: null
	});

	await vi.waitFor(() =>
		expect(screen.container.querySelector('[data-testid="task-restart-button"]')).toBeNull()
	);
});

// A rejected restart that is not a 409 (the task vanished, the Hub faulted) reaches the same
// region rather than the console.
test('a non-409 status on restart is presented in the shared region', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });
	getTaskDetailMock.mockResolvedValue(detail({ status: 'failed' }));
	restartTaskMock.mockRejectedValue(
		new FakeIngestSubmissionApiError('This task no longer exists.', 404)
	);

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });
	await screen.getByTestId('task-restart-button').click();

	await expect
		.element(screen.getByTestId('task-restart-error'))
		.toHaveTextContent('This task no longer exists.');
});
