import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import type { BoardResponse, BoardTask } from '$lib/types';

// T080 (Convergence) - the route composition introduced by T079 (submission form + Kanban
// board merged onto `/`) had no test coverage of its own: only the constituent
// SubmissionForm/KanbanColumn components and the pure ingestLifecycleClient helpers were
// tested in isolation. This exercises the actual `onMount` wiring and `tasksByStage`
// derivation in +page.svelte.
// 023 T048 (US5/AC1, FR-010..FR-012): the board owns the post-restart refresh, so a
// rejected restart on a card ends with the board re-fetching the task's true status.

const { onTasksChangedHandlers, startMock, stopMock } = vi.hoisted(() => ({
	onTasksChangedHandlers: [] as Array<(tasks: BoardTask[]) => void>,
	startMock: vi.fn(),
	stopMock: vi.fn()
}));

const { restartTaskMock, getBoardMock, TestApiError } = vi.hoisted(() => ({
	restartTaskMock: vi.fn(),
	getBoardMock: vi.fn(),
	TestApiError: class extends Error {
		constructor(
			message: string,
			public readonly status: number
		) {
			super(message);
			this.name = 'IngestSubmissionApiError';
		}
	}
}));

vi.mock('$lib/services/ingestSubmissionsApi', async (importOriginal) => ({
	...(await importOriginal<typeof import('$lib/services/ingestSubmissionsApi')>()),
	getBoard: () => getBoardMock(),
	restartTask: (taskId: string) => restartTaskMock(taskId),
	IngestSubmissionApiError: TestApiError
}));

beforeEach(() => {
	restartTaskMock.mockReset();
	restartTaskMock.mockResolvedValue({ taskId: 'task-1', status: 'queued' });
	getBoardMock.mockReset();
	getBoardMock.mockResolvedValue({ tasks: [], queuePaused: false } satisfies BoardResponse);
});

vi.mock('$lib/services/ingestLifecycleClient', () => ({
	createBoardLifecycleStream: (onTasksChanged: (tasks: BoardTask[]) => void) => {
		onTasksChangedHandlers.push(onTasksChanged);
		return {
			start: async () => {
				startMock();
			},
			stop: async () => {
				stopMock();
			}
		};
	}
}));

function task(overrides: Partial<BoardTask>): BoardTask {
	return {
		taskId: 'task-1',
		status: 'received',
		title: 'task-1',
		updatedAt: new Date().toISOString(),
		failureReason: null,
		taskLink: '/api/ingest-submissions/task-1',
		...overrides
	};
}

test('renders the submission form and the kanban board on the same page and starts the lifecycle stream', async () => {
	onTasksChangedHandlers.length = 0;
	const screen = await render(Page);

	await expect.element(screen.getByTestId('submission-form')).toBeVisible();
	await expect.element(screen.getByTestId('kanban-board')).toBeVisible();
	expect(startMock).toHaveBeenCalled();
});

// T103 (Convergence) - the query surface was previously unreachable from the root UI
// (no nav link anywhere in the app); this asserts the link exists and points to /query.
test('renders a nav link to the query surface', async () => {
	const screen = await render(Page);

	const link = screen.getByTestId('nav-link-query');
	await expect.element(link).toBeVisible();
	await expect.element(link).toHaveAttribute('href', '/query');
});

test('a lifecycle stream update buckets the task into its stage column, live', async () => {
	onTasksChangedHandlers.length = 0;
	const screen = await render(Page);
	const onTasksChanged = onTasksChangedHandlers.at(-1);
	if (!onTasksChanged) throw new Error('createBoardLifecycleStream was never started');

	onTasksChanged([task({ taskId: 'live-1', title: 'Live Article', status: 'converting' })]);

	await expect
		.poll(() => screen.container.querySelector('[data-stage="converting"]')?.textContent ?? '')
		.toContain('Live Article');

	const receivedColumn = screen.container.querySelector('[data-stage="received"]');
	expect(receivedColumn?.textContent ?? '').not.toContain('Live Article');
});

test('unmounting the page stops the lifecycle stream', async () => {
	onTasksChangedHandlers.length = 0;
	stopMock.mockClear();
	const screen = await render(Page);

	await screen.unmount();

	expect(stopMock).toHaveBeenCalled();
});

// ── 023 T048: restart from the board (FR-010, FR-011, FR-012) ────────────────────────

test('a failed task on the board renders a restart control; other stages do not', async () => {
	onTasksChangedHandlers.length = 0;
	const screen = await render(Page);
	const onTasksChanged = onTasksChangedHandlers.at(-1);
	if (!onTasksChanged) throw new Error('createBoardLifecycleStream was never started');

	onTasksChanged([
		task({ taskId: 'board-failed', title: 'Broken source', status: 'failed' }),
		task({ taskId: 'board-running', title: 'Live source', status: 'running' })
	]);

	await expect
		.poll(
			() => screen.container.querySelectorAll('[data-testid="task-card-restart-button"]').length
		)
		.toBe(1);

	const failedColumn = screen.container.querySelector('[data-stage="failed"]');
	expect(failedColumn?.querySelector('[data-testid="task-card-restart-button"]')).not.toBeNull();
});

// A restart that loses the race 409s because the task has already moved on. The point of
// the board-owned refresh is that the operator stops looking at a stale `failed` card and
// starts looking at where the task actually is — the inline error itself is asserted in
// TaskCard.svelte.test.ts, where the card survives the refresh.
test('a rejected restart on a board card makes the board re-fetch the true status', async () => {
	restartTaskMock.mockRejectedValue(new TestApiError('Task is not in a failed state.', 409));
	getBoardMock.mockResolvedValue({
		tasks: [task({ taskId: 'board-raced', title: 'Raced source', status: 'queued' })],
		queuePaused: false
	} satisfies BoardResponse);

	onTasksChangedHandlers.length = 0;
	const screen = await render(Page);
	const onTasksChanged = onTasksChangedHandlers.at(-1);
	if (!onTasksChanged) throw new Error('createBoardLifecycleStream was never started');

	onTasksChanged([task({ taskId: 'board-raced', title: 'Raced source', status: 'failed' })]);

	// The mount-time board read has already happened; count refreshes from here.
	await expect.poll(() => getBoardMock.mock.calls.length).toBeGreaterThan(0);
	const callsBeforeClick = getBoardMock.mock.calls.length;

	await screen.getByTestId('task-card-restart-button').click();

	await expect.poll(() => getBoardMock.mock.calls.length).toBeGreaterThan(callsBeforeClick);
	await expect
		.poll(() => screen.container.querySelector('[data-stage="queued"]')?.textContent ?? '')
		.toContain('Raced source');
	expect(screen.container.querySelector('[data-stage="failed"]')?.textContent ?? '').not.toContain(
		'Raced source'
	);
});
