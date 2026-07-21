import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import TaskCard from './TaskCard.svelte';
import type { BoardTask } from '$lib/types';

// T053 (US3): a failed TaskCard renders the reason and a link to the full Task Artifact, visually
// distinct from a completed card (SC-005).

test('failed task renders the failure reason and a details link', async () => {
	const task: BoardTask = {
		taskId: 'task-failed-1',
		status: 'failed',
		title: 'broken.pdf',
		updatedAt: new Date().toISOString(),
		failureReason: 'PdfReadError: unable to parse cross-reference table',
		taskLink: '/api/ingest-submissions/task-failed-1'
	};

	const screen = await render(TaskCard, { task });

	await expect.element(screen.getByTestId('status-badge')).toHaveTextContent('Failed');
	await expect
		.element(screen.getByTestId('task-card-failure-reason'))
		.toHaveTextContent('PdfReadError: unable to parse cross-reference table');
	// 006: Details links to the rendered internal route, built from taskId — not taskLink
	// (which stays pointed at the Hub JSON API for machine consumers).
	await expect
		.element(screen.getByTestId('task-card-link'))
		.toHaveAttribute('href', `/tasks/${task.taskId}`);
});

test('completed task does not render a failure reason', async () => {
	const task: BoardTask = {
		taskId: 'task-completed-1',
		status: 'completed',
		title: 'article.md',
		updatedAt: new Date().toISOString(),
		failureReason: null,
		taskLink: '/api/ingest-submissions/task-completed-1'
	};

	const screen = await render(TaskCard, { task });

	await expect.element(screen.getByTestId('status-badge')).toHaveTextContent('Completed');
	await expect.element(screen.getByTestId('task-card-failure-reason')).not.toBeInTheDocument();
});
