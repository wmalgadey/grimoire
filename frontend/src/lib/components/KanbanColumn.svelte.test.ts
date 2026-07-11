import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import KanbanColumn from './KanbanColumn.svelte';
import type { BoardTask } from '$lib/types';

// T041 (US2): KanbanColumn groups multiple in-flight tasks by stage and reflects task updates
// live when its `tasks` prop changes (as the board route does when a lifecycle event arrives).

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

test('renders every task passed to the column exactly once', async () => {
	const tasks = [
		task({ taskId: 'a', title: 'Article A' }),
		task({ taskId: 'b', title: 'Article B' })
	];
	const screen = await render(KanbanColumn, { stage: 'queued', tasks });

	await expect.element(screen.getByTestId('kanban-column-count')).toHaveTextContent('2');
	await expect.element(screen.getByText('Article A')).toBeVisible();
	await expect.element(screen.getByText('Article B')).toBeVisible();
});

test('re-rendering with an updated task list moves cards without a page reload', async () => {
	let tasks = [task({ taskId: 'a', title: 'Article A', status: 'queued' })];
	const screen = await render(KanbanColumn, { stage: 'queued', tasks });
	await expect.element(screen.getByTestId('kanban-column-count')).toHaveTextContent('1');

	// Simulate the task moving out of this stage (as a live lifecycle event would drive).
	tasks = [];
	await screen.rerender({ stage: 'queued', tasks });

	await expect.element(screen.getByTestId('kanban-column-count')).toHaveTextContent('0');
});
