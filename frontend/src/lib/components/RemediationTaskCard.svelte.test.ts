import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import RemediationTaskCard from './RemediationTaskCard.svelte';
import type { RemediationTaskBoardEntry } from '$lib/types';
import { RemediationApiError } from '$lib/services/remediationApi';

// T026 (015-lint-board-parity, US3)/T037 (US4): the board's remediation proposal card —
// verbatim agent-authored title, originating-run subtitle, distinct kind label (FR-006),
// live authorize/dismiss/withdraw actions wired to the real endpoints (FR-009/FR-010/
// FR-016), waiting-vs-executing visual distinction with queue position (FR-017), and
// surfaced outcome reasons (FR-005/FR-018).

const { authorizeMock, dismissMock, withdrawMock } = vi.hoisted(() => ({
	authorizeMock: vi.fn(),
	dismissMock: vi.fn(),
	withdrawMock: vi.fn()
}));

vi.mock('$lib/services/remediationApi', async (importOriginal) => {
	const actual = await importOriginal<typeof import('$lib/services/remediationApi')>();
	return {
		...actual,
		authorizeRemediationTask: authorizeMock,
		dismissRemediationTask: dismissMock,
		withdrawRemediationTaskAuthorization: withdrawMock
	};
});

function task(overrides: Partial<RemediationTaskBoardEntry>): RemediationTaskBoardEntry {
	return {
		kind: 'remediation_task',
		taskId: '2026-08-01-remediation-abc123',
		runId: '2026-08-01-lint-def456',
		title: 'Add missing tags to [[runtime-paths]]',
		state: 'proposed',
		proposedAt: new Date().toISOString(),
		queuePosition: null,
		outcomeReason: null,
		updatedAt: new Date().toISOString(),
		...overrides
	};
}

test('proposed task: renders the verbatim title, run subtitle, kind label, and live review actions', async () => {
	const screen = await render(RemediationTaskCard, { task: task({}) });

	await expect
		.element(screen.getByTestId('remediation-task-card-title'))
		.toHaveTextContent('Add missing tags to [[runtime-paths]]');
	await expect
		.element(screen.getByTestId('remediation-task-card-run'))
		.toHaveTextContent('From run 2026-08-01-lint-def456');
	// FR-006: the card names its activity kind, at a glance.
	await expect
		.element(screen.getByTestId('remediation-task-card'))
		.toHaveTextContent('Remediation proposal');

	const state = screen.getByTestId('remediation-task-card-state');
	await expect.element(state).toHaveTextContent('Proposed');
	await expect.element(state).toHaveAttribute('data-state', 'proposed');

	// T037: review actions are live, and enabled.
	await expect.element(screen.getByTestId('remediation-task-card-authorize')).not.toBeDisabled();
	await expect.element(screen.getByTestId('remediation-task-card-dismiss')).not.toBeDisabled();
});

test('proposed task: clicking Authorize calls the endpoint with the task id', async () => {
	authorizeMock.mockResolvedValueOnce({
		taskId: '2026-08-01-remediation-abc123',
		state: 'authorized',
		authorizedAt: new Date().toISOString(),
		queuePosition: 1
	});

	const screen = await render(RemediationTaskCard, { task: task({}) });
	await screen.getByTestId('remediation-task-card-authorize').click();

	expect(authorizeMock).toHaveBeenCalledExactlyOnceWith('2026-08-01-remediation-abc123');
	await expect.element(screen.getByTestId('remediation-task-card-error')).not.toBeInTheDocument();
});

test('proposed task: clicking Dismiss calls the endpoint with the task id', async () => {
	dismissMock.mockResolvedValueOnce({
		taskId: '2026-08-01-remediation-abc123',
		state: 'dismissed',
		dismissedAt: new Date().toISOString()
	});

	const screen = await render(RemediationTaskCard, { task: task({}) });
	await screen.getByTestId('remediation-task-card-dismiss').click();

	expect(dismissMock).toHaveBeenCalledExactlyOnceWith('2026-08-01-remediation-abc123');
});

test('proposed task: a rejected authorize (lost CAS race) surfaces the human-readable reason, never silently', async () => {
	authorizeMock.mockRejectedValueOnce(
		new RemediationApiError(
			'Only a proposed task can be authorized. This task is dismissed.',
			409,
			'task_not_proposed'
		)
	);

	const screen = await render(RemediationTaskCard, { task: task({}) });
	await screen.getByTestId('remediation-task-card-authorize').click();

	await expect
		.element(screen.getByTestId('remediation-task-card-error'))
		.toHaveTextContent('Only a proposed task can be authorized');
});

test('authorized task: shows the waiting state with its queue position and a live withdraw action', async () => {
	const screen = await render(RemediationTaskCard, {
		task: task({ state: 'authorized', queuePosition: 2 })
	});

	await expect
		.element(screen.getByTestId('remediation-task-card-state'))
		.toHaveTextContent('Waiting');
	await expect
		.element(screen.getByTestId('remediation-task-card-queue-position'))
		.toHaveTextContent('#2');

	// T037 (FR-016): withdrawal is live while waiting — no authorize/dismiss here.
	await expect.element(screen.getByTestId('remediation-task-card-withdraw')).not.toBeDisabled();
	await expect
		.element(screen.getByTestId('remediation-task-card-authorize'))
		.not.toBeInTheDocument();
	await expect.element(screen.getByTestId('remediation-task-card-dismiss')).not.toBeInTheDocument();
});

test('authorized task: clicking Withdraw calls the endpoint with the task id', async () => {
	withdrawMock.mockResolvedValueOnce({
		taskId: '2026-08-01-remediation-abc123',
		state: 'proposed'
	});

	const screen = await render(RemediationTaskCard, { task: task({ state: 'authorized' }) });
	await screen.getByTestId('remediation-task-card-withdraw').click();

	expect(withdrawMock).toHaveBeenCalledExactlyOnceWith('2026-08-01-remediation-abc123');
});

test('authorized task: a withdrawal that lost the race against dispatch surfaces execution_already_started', async () => {
	withdrawMock.mockRejectedValueOnce(
		new RemediationApiError(
			'The agent already began executing this task; it will run to a terminal outcome and can no longer be cancelled.',
			409,
			'execution_already_started'
		)
	);

	const screen = await render(RemediationTaskCard, { task: task({ state: 'authorized' }) });
	await screen.getByTestId('remediation-task-card-withdraw').click();

	await expect
		.element(screen.getByTestId('remediation-task-card-error'))
		.toHaveTextContent('already began executing');
});

test('executing task: visually distinct from waiting, no queue position, no review actions', async () => {
	const screen = await render(RemediationTaskCard, { task: task({ state: 'executing' }) });

	const state = screen.getByTestId('remediation-task-card-state');
	await expect.element(state).toHaveTextContent('Executing');
	await expect.element(state).toHaveAttribute('data-state', 'executing');
	await expect
		.element(screen.getByTestId('remediation-task-card-queue-position'))
		.not.toBeInTheDocument();
	await expect.element(screen.getByTestId('remediation-task-card-actions')).not.toBeInTheDocument();
});

test('completed task: no review actions, no outcome reason shown', async () => {
	const screen = await render(RemediationTaskCard, { task: task({ state: 'completed' }) });

	await expect
		.element(screen.getByTestId('remediation-task-card-state'))
		.toHaveTextContent('Completed');
	await expect.element(screen.getByTestId('remediation-task-card-actions')).not.toBeInTheDocument();
	await expect
		.element(screen.getByTestId('remediation-task-card-outcome-reason'))
		.not.toBeInTheDocument();
});

test('failed task: surfaces the outcome reason (FR-005)', async () => {
	const screen = await render(RemediationTaskCard, {
		task: task({ state: 'failed', outcomeReason: 'The guarded write was denied.' })
	});

	await expect
		.element(screen.getByTestId('remediation-task-card-state'))
		.toHaveTextContent('Failed');
	await expect
		.element(screen.getByTestId('remediation-task-card-outcome-reason'))
		.toHaveTextContent('The guarded write was denied.');
});

test('not-applicable task: surfaces the agent staleness reason (FR-018)', async () => {
	const screen = await render(RemediationTaskCard, {
		task: task({
			state: 'not_applicable',
			outcomeReason: 'The page gained a tags list after this action was proposed.'
		})
	});

	await expect
		.element(screen.getByTestId('remediation-task-card-state'))
		.toHaveTextContent('Not applicable');
	await expect
		.element(screen.getByTestId('remediation-task-card-outcome-reason'))
		.toHaveTextContent('gained a tags list');
});

test('dismissed task: no review actions, no outcome reason shown (human decision, no agent reason)', async () => {
	const screen = await render(RemediationTaskCard, { task: task({ state: 'dismissed' }) });

	await expect
		.element(screen.getByTestId('remediation-task-card-state'))
		.toHaveTextContent('Dismissed');
	await expect.element(screen.getByTestId('remediation-task-card-actions')).not.toBeInTheDocument();
});
