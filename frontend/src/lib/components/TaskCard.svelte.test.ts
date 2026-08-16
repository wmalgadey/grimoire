import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import TaskCard from './TaskCard.svelte';
import type { BoardTask } from '$lib/types';

// 023 T048 (US5/AC1, FR-010..FR-012): the restart action lives on the board card too, not
// only on the detail page — the operator sees the failed task on the board and must be able
// to act on it there. `restartTask` is the real client module, replaced here so the card's
// own call/disable/error behavior is observable without a Hub.
const { restartTaskMock, TestApiError } = vi.hoisted(() => ({
	restartTaskMock: vi.fn(),
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

vi.mock('$lib/services/ingestSubmissionsApi', () => ({
	restartTask: (taskId: string) => restartTaskMock(taskId),
	IngestSubmissionApiError: TestApiError
}));

beforeEach(() => {
	restartTaskMock.mockReset();
	restartTaskMock.mockResolvedValue({ taskId: 'task-1', status: 'queued' });
});

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

// ── 023 T048: restart control on the board card (FR-010, FR-011, FR-012) ─────────────

test('a failed card renders a restart control', async () => {
	const screen = await render(TaskCard, { task: task({ status: 'failed' }) });

	await expect.element(screen.getByTestId('task-card-restart-button')).toBeVisible();
});

test.for(['received', 'converting', 'queued', 'running', 'completed'] as const)(
	'a %s card renders no restart control (FR-011)',
	async (status) => {
		const screen = await render(TaskCard, { task: task({ status }) });

		await expect.element(screen.getByTestId('task-card-restart-button')).not.toBeInTheDocument();
	}
);

test('clicking restart calls restartTask once with the card task id', async () => {
	const screen = await render(TaskCard, {
		task: task({ taskId: 'task-failed-9', status: 'failed' })
	});

	await screen.getByTestId('task-card-restart-button').click();

	await expect.poll(() => restartTaskMock.mock.calls.length).toBe(1);
	expect(restartTaskMock).toHaveBeenCalledWith('task-failed-9');
});

test('the restart control is disabled while the request is in flight (FR-012)', async () => {
	let release!: () => void;
	restartTaskMock.mockImplementation(
		() =>
			new Promise((resolve) => {
				release = () => resolve({ taskId: 'task-1', status: 'queued' });
			})
	);

	const screen = await render(TaskCard, { task: task({ status: 'failed' }) });
	const button = screen.getByTestId('task-card-restart-button');
	await button.click();

	await expect.element(button).toBeDisabled();

	release();
	await expect.element(button).not.toBeDisabled();
});

test('a 409 rejection surfaces an inline error and asks the board to refresh (FR-012)', async () => {
	restartTaskMock.mockRejectedValue(new TestApiError('Task is not in a failed state.', 409));
	const onRefreshRequested = vi.fn();

	const screen = await render(TaskCard, {
		task: task({ taskId: 'task-raced', status: 'failed' }),
		onRefreshRequested
	});

	await screen.getByTestId('task-card-restart-button').click();

	await expect
		.element(screen.getByTestId('task-card-restart-error'))
		.toHaveTextContent('Task is not in a failed state.');
	// The card never trusts its own click: the operator must end up seeing the task's real
	// current status, which only the board can re-fetch.
	await expect.poll(() => onRefreshRequested.mock.calls.length).toBe(1);
});

test('the restart control adds no status text to the card (FR-009/SC-006)', async () => {
	const screen = await render(TaskCard, { task: task({ status: 'failed', title: 'A label' }) });

	await expect.element(screen.getByTestId('task-card-restart-button')).toBeVisible();
	const card = screen.getByTestId('task-card').element();
	expect(card.textContent).not.toMatch(/\b(Received|Converting|Queued|Running|Completed|Failed)\b/);
});
