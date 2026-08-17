import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import BoardLane from './BoardLane.svelte';
import { toIngestItem } from '$lib/board';
import type { BoardTask } from '$lib/types';

// Ports T041 (US2) and 023 T034 (US6, FR-009/SC-006) from the retired KanbanColumn onto the
// lane that replaced it — a lane still groups the tasks it is given, reflects a changed list
// live, and names its own stage — plus what the design added: collapsing into a rail, and a
// cap on how many cards a deep queue draws.

function item(overrides: Partial<BoardTask> = {}) {
	return toIngestItem({
		taskId: 'task-1',
		status: 'queued',
		title: 'task-1',
		updatedAt: new Date().toISOString(),
		failureReason: null,
		taskLink: '/api/ingest-submissions/task-1',
		...overrides
	});
}

const noop = () => {};

test('renders every item passed to the lane exactly once', async () => {
	const screen = await render(BoardLane, {
		stage: 'queued',
		items: [item({ taskId: 'a', title: 'Article A' }), item({ taskId: 'b', title: 'Article B' })],
		collapsed: false,
		onToggle: noop,
		onOpenCard: noop
	});

	await expect.element(screen.getByTestId('kanban-column-count')).toHaveTextContent('2');
	await expect.element(screen.getByText('Article A')).toBeVisible();
	await expect.element(screen.getByText('Article B')).toBeVisible();
});

test('re-rendering with an updated list moves cards without a page reload', async () => {
	const props = {
		stage: 'queued' as const,
		items: [item({ taskId: 'a', title: 'Article A' })],
		collapsed: false,
		onToggle: noop,
		onOpenCard: noop
	};
	const screen = await render(BoardLane, props);
	await expect.element(screen.getByTestId('kanban-column-count')).toHaveTextContent('1');

	await screen.rerender({ ...props, items: [] });

	await expect.element(screen.getByTestId('kanban-column-count')).toHaveTextContent('0');
});

test.for([
	['received', 'Received'],
	['converting', 'Convert'],
	['queued', 'Queued'],
	['running', 'Running'],
	['completed', 'Done'],
	['failed', 'Failed']
] as const)('the %s lane header still names its stage', async ([stage, label]) => {
	const screen = await render(BoardLane, {
		stage,
		items: [item({ taskId: `t-${stage}`, status: stage })],
		collapsed: false,
		onToggle: noop,
		onOpenCard: noop
	});

	const column = screen.getByTestId('kanban-column').element();
	expect(column.querySelector('h2')?.textContent?.trim()).toBe(label);
});

test('a collapsed lane becomes a rail carrying its name and count, and no cards', async () => {
	const screen = await render(BoardLane, {
		stage: 'failed',
		items: [item({ taskId: 'a', title: 'Broken source', status: 'failed' })],
		collapsed: true,
		onToggle: noop,
		onOpenCard: noop
	});

	const rail = screen.getByTestId('kanban-column-rail');
	await expect.element(rail).toHaveTextContent('Failed');
	await expect.element(rail).toHaveTextContent('1');
	await expect.element(screen.getByTestId('board-card')).not.toBeInTheDocument();
});

test('the rail reopens the lane, and the lane header collapses it again', async () => {
	const onToggle = vi.fn();
	const collapsed = await render(BoardLane, {
		stage: 'failed',
		items: [],
		collapsed: true,
		onToggle,
		onOpenCard: noop
	});
	await collapsed.getByTestId('kanban-column-rail').click();
	expect(onToggle).toHaveBeenCalledOnce();
	collapsed.unmount();

	const expanded = await render(BoardLane, {
		stage: 'failed',
		items: [],
		collapsed: false,
		onToggle,
		onOpenCard: noop
	});
	await expanded.getByTestId('kanban-column-collapse').click();
	expect(onToggle).toHaveBeenCalledTimes(2);
});

// The scaling complaint the redesign started from: a deep queue must not push the other
// lanes off the screen, but the count still tells the truth about what is in there.
test('a lane past its cap draws only the first cards, with the rest behind a "+N more"', async () => {
	const items = Array.from({ length: 9 }, (_, i) =>
		item({ taskId: `q-${i}`, title: `Queued ${i}` })
	);
	const screen = await render(BoardLane, {
		stage: 'queued',
		items,
		collapsed: false,
		onToggle: noop,
		onOpenCard: noop,
		maxVisible: 5
	});

	await expect.element(screen.getByTestId('kanban-column-count')).toHaveTextContent('9');
	expect(screen.container.querySelectorAll('[data-testid="board-card"]').length).toBe(5);

	await screen.getByTestId('kanban-column-overflow').click();

	await expect
		.poll(() => screen.container.querySelectorAll('[data-testid="board-card"]').length)
		.toBe(9);
});

test('an empty expanded lane says so', async () => {
	const screen = await render(BoardLane, {
		stage: 'running',
		items: [],
		collapsed: false,
		onToggle: noop,
		onOpenCard: noop,
		emptyText: 'nothing running'
	});

	await expect
		.element(screen.getByTestId('kanban-column-empty'))
		.toHaveTextContent('nothing running');
});
