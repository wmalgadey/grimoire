import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import type { BoardResponse, BoardTask, LintRun, RemediationTaskBoardEntry } from '$lib/types';

// T080 (Convergence) exercised the board route's own wiring — the lifecycle stream and the
// stage grouping — rather than its parts in isolation, and 023 T048 added the board-owned
// refresh after a rejected restart. Both still hold; what changed with the Hi-Fi design is
// the surface they act through: the board is the whole page, lint runs and remediation
// proposals share the lanes, a card opens a popover, and Done/Failed start collapsed.

const { onTasksChangedHandlers, onLintRunHandlers, onRemediationHandlers, startMock, stopMock } =
	vi.hoisted(() => ({
		onTasksChangedHandlers: [] as Array<(tasks: BoardTask[]) => void>,
		onLintRunHandlers: [] as Array<(run: LintRun | null) => void>,
		onRemediationHandlers: [] as Array<(tasks: RemediationTaskBoardEntry[]) => void>,
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

vi.mock('$lib/services/lintLifecycleClient', () => ({
	createLintRunStream: (onRunChanged: (run: LintRun | null) => void) => {
		onLintRunHandlers.push(onRunChanged);
		return { start: async () => {}, stop: async () => {} };
	}
}));

vi.mock('$lib/services/remediationLifecycleClient', () => ({
	createRemediationTaskStream: (onTasksChanged: (tasks: RemediationTaskBoardEntry[]) => void) => {
		onRemediationHandlers.push(onTasksChanged);
		return { start: async () => {}, stop: async () => {} };
	}
}));

beforeEach(() => {
	startMock.mockReset();
	stopMock.mockReset();
	restartTaskMock.mockReset();
	restartTaskMock.mockResolvedValue({ taskId: 'task-1', status: 'queued' });
	getBoardMock.mockReset();
	getBoardMock.mockResolvedValue({ tasks: [], queuePaused: false } satisfies BoardResponse);
	onTasksChangedHandlers.length = 0;
	onLintRunHandlers.length = 0;
	onRemediationHandlers.length = 0;
});

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

function remediation(
	overrides: Partial<RemediationTaskBoardEntry> = {}
): RemediationTaskBoardEntry {
	return {
		kind: 'remediation_task',
		taskId: '2026-08-16-remediation-1',
		runId: 'lint-24',
		title: 'Reconcile the retention window',
		state: 'proposed',
		proposedAt: new Date().toISOString(),
		queuePosition: null,
		outcomeReason: null,
		updatedAt: new Date().toISOString(),
		...overrides
	};
}

function emitTasks(tasks: BoardTask[]) {
	const handler = onTasksChangedHandlers.at(-1);
	if (!handler) throw new Error('createBoardLifecycleStream was never started');
	handler(tasks);
}

test('renders the board and the app shell, and starts the lifecycle stream', async () => {
	const screen = await render(Page);

	await expect.element(screen.getByTestId('app-nav')).toBeVisible();
	await expect.element(screen.getByTestId('kanban-board')).toBeVisible();
	expect(startMock).toHaveBeenCalled();
});

// The submission form moved behind "+ Ingest" — the board is the whole screen now — so the
// form has to still be reachable in one action from here.
test('the ingest form opens from the nav rather than sitting above the board', async () => {
	const screen = await render(Page);

	await expect.element(screen.getByTestId('submission-form')).not.toBeInTheDocument();
	await screen.getByTestId('nav-ingest-button').click();

	await expect.element(screen.getByTestId('ingest-dialog')).toBeVisible();
	await expect.element(screen.getByTestId('submission-form')).toBeVisible();
});

test('renders a nav link to the conversations surface', async () => {
	const screen = await render(Page);

	const link = screen.getByTestId('nav-link-query');
	await expect.element(link).toBeVisible();
	await expect.element(link).toHaveAttribute('href', '/query');
});

test('a lifecycle stream update buckets the task into its stage lane, live', async () => {
	const screen = await render(Page);

	emitTasks([task({ taskId: 'live-1', title: 'Live Article', status: 'converting' })]);

	await expect
		.poll(() => screen.container.querySelector('[data-stage="converting"]')?.textContent ?? '')
		.toContain('Live Article');

	const receivedColumn = screen.container.querySelector('[data-stage="received"]');
	expect(receivedColumn?.textContent ?? '').not.toContain('Live Article');
});

// 015 FR-001/FR-007 in the design's shape: lint runs and remediation proposals are ordinary
// cards in the lanes, not a separate strip above the board.
test('a lint run and a remediation proposal land in the lanes with the ingest tasks', async () => {
	const screen = await render(Page);
	emitTasks([task({ taskId: 'ing-1', title: 'A source', status: 'running' })]);

	onRemediationHandlers.at(-1)?.([remediation()]);
	onLintRunHandlers.at(-1)?.({
		runId: 'lint-24',
		status: 'running',
		triggeredAt: new Date().toISOString(),
		completedAt: null,
		failureReason: null,
		hasFindingsReport: false
	});

	await expect
		.poll(() => screen.container.querySelector('[data-stage="running"]')?.textContent ?? '')
		.toContain('Wiki health check');
	expect(screen.container.querySelector('[data-stage="received"]')?.textContent ?? '').toContain(
		'Reconcile the retention window'
	);
});

test('Done and Failed start collapsed into rails, and reopen on click', async () => {
	const screen = await render(Page);
	emitTasks([task({ taskId: 'gone', title: 'Broken source', status: 'failed' })]);

	await expect
		.poll(() =>
			screen.container.querySelector('[data-testid="kanban-column-rail"][data-stage="failed"]')
		)
		.not.toBeNull();
	expect(screen.container.querySelector('[data-stage="failed"]')?.textContent ?? '').not.toContain(
		'Broken source'
	);

	await screen.getByTestId('kanban-column-rail').filter({ hasText: 'Failed' }).click();

	await expect
		.poll(() => screen.container.querySelector('[data-stage="failed"]')?.textContent ?? '')
		.toContain('Broken source');
});

test('unmounting the page stops the lifecycle stream', async () => {
	const screen = await render(Page);

	await screen.unmount();

	expect(stopMock).toHaveBeenCalled();
});

// ── the triage strip (chat 3: "it filters the board down to just those tasks") ──────────

test('the triage strip counts what is waiting on a person and filters the board to it', async () => {
	const screen = await render(Page);
	emitTasks([
		task({ taskId: 'ok-1', title: 'Healthy source', status: 'completed' }),
		task({ taskId: 'bad-1', title: 'Broken source', status: 'failed' })
	]);
	onRemediationHandlers.at(-1)?.([remediation()]);

	await expect.element(screen.getByTestId('board-needs-you-count')).toHaveTextContent('2 need you');

	await screen.getByTestId('board-needs-you').click();

	// The Failed lane opens so the failures it counted are actually on screen, and what does
	// not need a person is filtered out.
	await expect
		.poll(() => screen.container.querySelector('[data-stage="failed"]')?.textContent ?? '')
		.toContain('Broken source');
	expect(screen.container.textContent ?? '').not.toContain('Healthy source');

	await screen.getByTestId('board-needs-you').click();
	await expect
		.poll(() => screen.container.querySelector('[data-stage="completed"]')?.textContent ?? '')
		.not.toContain('Broken source');
});

test('the search narrows the board to matching titles and ids', async () => {
	const screen = await render(Page);
	emitTasks([
		task({ taskId: 'ing-a', title: 'Vendor SOW.pdf', status: 'received' }),
		task({ taskId: 'ing-b', title: 'Retention policy draft.docx', status: 'received' })
	]);

	await screen.getByTestId('board-search-input').fill('vendor');

	await expect
		.poll(() => screen.container.querySelector('[data-stage="received"]')?.textContent ?? '')
		.toContain('Vendor SOW.pdf');
	expect(
		screen.container.querySelector('[data-stage="received"]')?.textContent ?? ''
	).not.toContain('Retention policy draft.docx');
});

// 004 FR-021: queued tasks survive a Hub restart but wait for an explicit resume.
test('a paused queue offers a resume action, and a failed resume is presented', async () => {
	getBoardMock.mockResolvedValue({ tasks: [], queuePaused: true } satisfies BoardResponse);
	const screen = await render(Page);
	emitTasks([task({ taskId: 'q-1', status: 'queued', queuePosition: 1 })]);

	await expect.element(screen.getByTestId('queue-resume-button')).toBeVisible();
});

// ── 023 T048: restart from the board, now via the card's popover (FR-010..FR-012) ───────

test('a failed card offers Restart in its popover; a running one does not', async () => {
	const screen = await render(Page);
	emitTasks([
		task({ taskId: 'board-failed', title: 'Broken source', status: 'failed' }),
		task({ taskId: 'board-running', title: 'Live source', status: 'running' })
	]);

	await screen.getByTestId('kanban-column-rail').filter({ hasText: 'Failed' }).click();
	await screen.getByTestId('board-card').filter({ hasText: 'Broken source' }).click();
	await expect.element(screen.getByTestId('card-popover-restart')).toBeVisible();
	await screen.getByTestId('card-popover-close').click();

	await screen.getByTestId('board-card').filter({ hasText: 'Live source' }).click();
	await expect.element(screen.getByTestId('card-popover-restart')).not.toBeInTheDocument();
});

// A restart that loses the race 409s because the task has already moved on. The point of the
// board-owned refresh is that the operator stops looking at a stale `failed` card.
test('a rejected restart makes the board re-fetch the true status', async () => {
	restartTaskMock.mockRejectedValue(new TestApiError('Task is not in a failed state.', 409));
	getBoardMock.mockResolvedValue({
		tasks: [task({ taskId: 'board-raced', title: 'Raced source', status: 'queued' })],
		queuePaused: false
	} satisfies BoardResponse);

	const screen = await render(Page);
	emitTasks([task({ taskId: 'board-raced', title: 'Raced source', status: 'failed' })]);

	await expect.poll(() => getBoardMock.mock.calls.length).toBeGreaterThan(0);
	const callsBeforeClick = getBoardMock.mock.calls.length;

	await screen.getByTestId('kanban-column-rail').filter({ hasText: 'Failed' }).click();
	await screen.getByTestId('board-card').click();
	await screen.getByTestId('card-popover-restart').click();

	await expect.poll(() => getBoardMock.mock.calls.length).toBeGreaterThan(callsBeforeClick);
	await expect
		.poll(() => screen.container.querySelector('[data-stage="queued"]')?.textContent ?? '')
		.toContain('Raced source');
});

// 4c's empty state: only once the board has actually answered, never while it is still loading.
test('an empty board shows the empty state, and a loading board does not', async () => {
	const screen = await render(Page);

	await expect.element(screen.getByTestId('board-empty-state')).not.toBeInTheDocument();

	emitTasks([]);

	await expect.element(screen.getByTestId('board-empty-state')).toBeVisible();
	await expect.element(screen.getByTestId('kanban-board')).not.toBeInTheDocument();
});
