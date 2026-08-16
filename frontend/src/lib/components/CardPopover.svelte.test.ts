import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import CardPopover from './CardPopover.svelte';
import { toIngestItem, toLintItem, toRemediationItem, type BoardItem } from '$lib/board';
import type { BoardTask, RemediationTaskBoardEntry } from '$lib/types';

// The card's quick view, and with it the actions that used to live on the cards themselves:
// restart (023 FR-010..FR-012, ported from TaskCard) and authorize/dismiss/withdraw
// (015 T037/FR-009/FR-010/FR-016, ported from RemediationTaskCard). None of them mutates the
// item — the board's streams stay authoritative — and every rejection is shown, never silent.

const { restartMock, authorizeMock, dismissMock, withdrawMock, ApiError } = vi.hoisted(() => ({
	restartMock: vi.fn(),
	authorizeMock: vi.fn(),
	dismissMock: vi.fn(),
	withdrawMock: vi.fn(),
	ApiError: class extends Error {
		constructor(
			message: string,
			public readonly status: number
		) {
			super(message);
			this.name = 'RemediationApiError';
		}
	}
}));

vi.mock('$lib/services/ingestSubmissionsApi', async (importOriginal) => ({
	...(await importOriginal<typeof import('$lib/services/ingestSubmissionsApi')>()),
	restartTask: (taskId: string) => restartMock(taskId)
}));

vi.mock('$lib/services/remediationApi', async (importOriginal) => ({
	...(await importOriginal<typeof import('$lib/services/remediationApi')>()),
	authorizeRemediationTask: (taskId: string) => authorizeMock(taskId),
	dismissRemediationTask: (taskId: string) => dismissMock(taskId),
	withdrawRemediationTaskAuthorization: (taskId: string) => withdrawMock(taskId)
}));

beforeEach(() => {
	for (const mock of [restartMock, authorizeMock, dismissMock, withdrawMock]) {
		mock.mockReset();
		mock.mockResolvedValue(undefined);
	}
});

function ingest(overrides: Partial<BoardTask> = {}, activity = null) {
	return toIngestItem(
		{
			taskId: 'ing-1',
			status: 'received',
			title: 'A source',
			updatedAt: '2026-08-16T14:00:00Z',
			failureReason: null,
			taskLink: '/api/ingest-submissions/ing-1',
			...overrides
		},
		activity
	);
}

function remediation(overrides: Partial<RemediationTaskBoardEntry> = {}) {
	return toRemediationItem({
		kind: 'remediation_task',
		taskId: '2026-08-16-remediation-1',
		runId: 'lint-24',
		title: 'Reconcile the retention window',
		state: 'proposed',
		proposedAt: '2026-08-16T13:41:00Z',
		queuePosition: null,
		outcomeReason: null,
		updatedAt: '2026-08-16T13:41:00Z',
		...overrides
	});
}

function open(item: BoardItem, props: Record<string, unknown> = {}) {
	return render(CardPopover, {
		item,
		position: { left: 20, top: 20 },
		onClose: () => {},
		...props
	});
}

test('a failed ingest offers Restart, which calls the Hub and asks the board to re-read', async () => {
	const onClose = vi.fn();
	const onRefreshRequested = vi.fn();
	const screen = await open(ingest({ status: 'failed', failureReason: 'Fetch failed: 403' }), {
		onClose,
		onRefreshRequested
	});

	await screen.getByTestId('card-popover-restart').click();

	expect(restartMock).toHaveBeenCalledWith('ing-1');
	await expect.poll(() => onRefreshRequested).toHaveBeenCalled();
	expect(onClose).toHaveBeenCalled();
});

test('a rejected restart is shown in the popover, which stays open', async () => {
	restartMock.mockRejectedValue(new ApiError('Task is not in a failed state.', 409));
	const onClose = vi.fn();
	const screen = await open(ingest({ status: 'failed', failureReason: 'Fetch failed: 403' }), {
		onClose
	});

	await screen.getByTestId('card-popover-restart').click();

	await expect
		.element(screen.getByTestId('card-popover-error'))
		.toHaveTextContent('Task is not in a failed state.');
	expect(onClose).not.toHaveBeenCalled();
});

test('the recorded failure reason is presented through the shared error component', async () => {
	const screen = await open(ingest({ status: 'failed', failureReason: 'Convert timeout' }));

	await expect
		.element(screen.getByTestId('card-popover-failure-reason'))
		.toHaveTextContent('Convert timeout');
});

test('a proposed remediation offers Authorize and Dismiss, wired to their endpoints', async () => {
	const authorized = await open(remediation());
	await authorized.getByTestId('card-popover-authorize').click();
	expect(authorizeMock).toHaveBeenCalledWith('2026-08-16-remediation-1');
	authorized.unmount();

	const dismissed = await open(remediation());
	await dismissed.getByTestId('card-popover-dismiss').click();
	expect(dismissMock).toHaveBeenCalledWith('2026-08-16-remediation-1');
});

test('an authorized remediation offers Withdraw instead', async () => {
	const screen = await open(remediation({ state: 'authorized', queuePosition: 1 }));

	await expect.element(screen.getByTestId('card-popover-authorize')).not.toBeInTheDocument();
	await screen.getByTestId('card-popover-withdraw').click();

	expect(withdrawMock).toHaveBeenCalledWith('2026-08-16-remediation-1');
});

test('a lost CAS race on a review action is shown, never silent', async () => {
	authorizeMock.mockRejectedValue(new ApiError('This task is no longer proposed.', 409));
	const screen = await open(remediation());

	await screen.getByTestId('card-popover-authorize').click();

	await expect
		.element(screen.getByTestId('card-popover-error'))
		.toHaveTextContent('This task is no longer proposed.');
});

// 004 FR-018: the live loop snapshot is the reason to open a running card at all.
test('a running task shows what it is doing now and what it has been calling', async () => {
	const screen = await open(
		ingest({ taskId: 'ing-run', status: 'running' }, {
			modelTurns: 7,
			toolCalls: 21,
			toolCallsByName: { read_wiki: 9, write_page: 2 },
			currentAction: 'writing wiki page'
		} as never)
	);

	const now = screen.getByTestId('card-popover-now');
	await expect.element(now).toHaveTextContent('writing wiki page');
	await expect.element(now).toHaveTextContent('7 model turns · 21 tool calls');
	await expect.element(now).toHaveTextContent('read_wiki ×9');
});

test('a completed lint run opens its findings report; one without a report does not offer it', async () => {
	const onShowFindings = vi.fn();
	const withReport = await open(
		toLintItem({
			runId: 'lint-24',
			status: 'completed',
			triggeredAt: '2026-08-16T13:35:00Z',
			completedAt: '2026-08-16T13:40:00Z',
			failureReason: null,
			hasFindingsReport: true
		}),
		{ onShowFindings }
	);
	await withReport.getByTestId('card-popover-findings').click();
	expect(onShowFindings).toHaveBeenCalledWith('lint-24');
	// A lint run has no detail route of its own — the board is where it lives.
	await expect.element(withReport.getByTestId('card-popover-details')).not.toBeInTheDocument();
	withReport.unmount();

	const withoutReport = await open(
		toLintItem({
			runId: 'lint-25',
			status: 'running',
			triggeredAt: '2026-08-16T14:00:00Z',
			completedAt: null,
			failureReason: null,
			hasFindingsReport: false
		}),
		{ onShowFindings }
	);
	await expect.element(withoutReport.getByTestId('card-popover-findings')).not.toBeInTheDocument();
});

test('Open details is the way into the detail view, for ingest and remediation alike', async () => {
	const task = await open(ingest());
	await expect
		.element(task.getByTestId('card-popover-details'))
		.toHaveAttribute('href', '/tasks/ing-1');
	task.unmount();

	const proposal = await open(remediation());
	await expect
		.element(proposal.getByTestId('card-popover-details'))
		.toHaveAttribute('href', '/tasks/2026-08-16-remediation-1');
});

test('the backdrop and Escape both dismiss the popover', async () => {
	const onClose = vi.fn();
	const screen = await open(ingest(), { onClose });

	// Dispatched rather than driven through the pointer: the backdrop is sized by a Tailwind
	// utility, and component tests render without that sheet, so it has no box to click.
	(screen.getByTestId('card-popover-backdrop').element() as HTMLElement).click();
	await expect.poll(() => onClose.mock.calls.length).toBe(1);

	window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
	await expect.poll(() => onClose.mock.calls.length).toBe(2);
});
