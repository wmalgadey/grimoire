import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import TaskCard from './TaskCard.svelte';
import type { BoardTask } from '$lib/types';

// T053 (US3): a failed TaskCard renders the reason and a link to the full Task Artifact, visually
// distinct from a completed card (SC-005).
// 023 T022 (US3, FR-003/FR-004, SC-003): the human-readable label is the card's primary text and
// the raw task id stays available underneath.
// 023 T034 (US6, FR-009, SC-006): cards carry no status label at all — the column alone conveys
// status, so the badge that used to duplicate it is gone.

function task(overrides: Partial<BoardTask> = {}): BoardTask {
	return {
		taskId: 'task-1',
		status: 'completed',
		title: 'Getting Started',
		updatedAt: new Date().toISOString(),
		failureReason: null,
		taskLink: '/api/ingest-submissions/task-1',
		...overrides
	};
}

test('failed task renders the failure reason and a details link', async () => {
	const failed = task({
		taskId: 'task-failed-1',
		status: 'failed',
		title: 'broken.pdf',
		failureReason: 'PdfReadError: unable to parse cross-reference table',
		taskLink: '/api/ingest-submissions/task-failed-1'
	});

	const screen = await render(TaskCard, { task: failed });

	await expect
		.element(screen.getByTestId('task-card-failure-reason'))
		.toHaveTextContent('PdfReadError: unable to parse cross-reference table');
	// 006: Details links to the rendered internal route, built from taskId — not taskLink
	// (which stays pointed at the Hub JSON API for machine consumers).
	await expect
		.element(screen.getByTestId('task-card-link'))
		.toHaveAttribute('href', `/tasks/${failed.taskId}`);
});

test('completed task does not render a failure reason', async () => {
	const screen = await render(TaskCard, { task: task({ title: 'article.md' }) });

	await expect.element(screen.getByTestId('task-card-failure-reason')).not.toBeInTheDocument();
});

test('renders the human-readable label as the primary text', async () => {
	const screen = await render(TaskCard, { task: task({ title: 'Getting Started' }) });

	await expect.element(screen.getByTestId('task-card-title')).toHaveTextContent('Getting Started');
});

test('keeps the raw task id visible alongside the label', async () => {
	const screen = await render(TaskCard, { task: task({ taskId: '2026-08-13-ingest-abc123' }) });

	await expect
		.element(screen.getByTestId('task-card-task-id'))
		.toHaveTextContent('2026-08-13-ingest-abc123');
});

test('renders no status badge — the column conveys the status (FR-009)', async () => {
	const screen = await render(TaskCard, { task: task({ status: 'running' }) });

	await expect.element(screen.getByTestId('status-badge')).not.toBeInTheDocument();
});

test.for(['received', 'converting', 'queued', 'running', 'completed', 'failed'] as const)(
	'renders no status text on a %s card',
	async (status) => {
		const screen = await render(TaskCard, { task: task({ status, title: 'A label' }) });

		const card = screen.getByTestId('task-card').element();
		expect(card.textContent).not.toMatch(
			/\b(Received|Converting|Queued|Running|Completed|Failed)\b/
		);
	}
);
