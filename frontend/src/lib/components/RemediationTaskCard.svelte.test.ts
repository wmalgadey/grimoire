import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import RemediationTaskCard from './RemediationTaskCard.svelte';
import type { RemediationTaskBoardEntry } from '$lib/types';

// T026 (015-lint-board-parity, US3): the board's remediation proposal card — verbatim
// agent-authored title, originating-run subtitle, distinct kind label (FR-006), review
// action placeholders while proposed (live actions arrive in US4/T037), and surfaced
// outcome reasons (FR-005/FR-018).

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

test('proposed task: renders the verbatim title, run subtitle, kind label, and placeholder review actions', async () => {
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

	// T026: review actions are placeholders until US4 wires them (T037).
	await expect.element(screen.getByTestId('remediation-task-card-authorize')).toBeDisabled();
	await expect.element(screen.getByTestId('remediation-task-card-dismiss')).toBeDisabled();
});

test('authorized task: shows the waiting state with its queue position, no review actions', async () => {
	const screen = await render(RemediationTaskCard, {
		task: task({ state: 'authorized', queuePosition: 2 })
	});

	await expect
		.element(screen.getByTestId('remediation-task-card-state'))
		.toHaveTextContent('Waiting');
	await expect
		.element(screen.getByTestId('remediation-task-card-queue-position'))
		.toHaveTextContent('#2');
	await expect.element(screen.getByTestId('remediation-task-card-actions')).not.toBeInTheDocument();
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
