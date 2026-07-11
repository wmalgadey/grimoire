import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import type { BoardTask } from '$lib/types';

// T080 (Convergence) - the route composition introduced by T079 (submission form + Kanban
// board merged onto `/`) had no test coverage of its own: only the constituent
// SubmissionForm/KanbanColumn components and the pure ingestLifecycleClient helpers were
// tested in isolation. This exercises the actual `onMount` wiring and `tasksByStage`
// derivation in +page.svelte.

const { onTasksChangedHandlers, startMock, stopMock } = vi.hoisted(() => ({
	onTasksChangedHandlers: [] as Array<(tasks: BoardTask[]) => void>,
	startMock: vi.fn(),
	stopMock: vi.fn()
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
