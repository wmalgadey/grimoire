import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import LintRunCard from './LintRunCard.svelte';
import type { LintRun } from '$lib/types';

// T015 (015-lint-board-parity, US1): the board's lint run card across all four states —
// no-run / running / completed / failed (+ reason) — visually distinct from ingest cards
// via its own kind label and testids (FR-001/FR-005/FR-006, SC-003 groundwork).

function run(overrides: Partial<LintRun>): LintRun {
	return {
		runId: '2026-08-01-lint-abc123',
		status: 'running',
		triggeredAt: new Date().toISOString(),
		completedAt: null,
		failureReason: null,
		hasFindingsReport: false,
		...overrides
	};
}

test('no run ever: renders the empty state instead of a status badge', async () => {
	const screen = await render(LintRunCard, { run: null });

	await expect
		.element(screen.getByTestId('lint-run-card-empty'))
		.toHaveTextContent('No lint activity yet');
	await expect.element(screen.getByTestId('lint-run-card-status')).not.toBeInTheDocument();
});

test('running run: renders the running status and the distinguishing kind label', async () => {
	const screen = await render(LintRunCard, { run: run({ status: 'running' }) });

	const status = screen.getByTestId('lint-run-card-status');
	await expect.element(status).toHaveTextContent('Running');
	await expect.element(status).toHaveAttribute('data-status', 'running');
	// FR-006: the card names its activity kind, at a glance.
	await expect.element(screen.getByTestId('lint-run-card')).toHaveTextContent('Wiki health check');
});

test('completed run: renders the completed status and a findings link', async () => {
	const screen = await render(LintRunCard, {
		run: run({
			status: 'completed',
			completedAt: new Date().toISOString(),
			hasFindingsReport: true
		})
	});

	await expect.element(screen.getByTestId('lint-run-card-status')).toHaveTextContent('Completed');
	await expect.element(screen.getByTestId('lint-run-card-link')).toHaveAttribute('href', '/lint');
	await expect.element(screen.getByTestId('lint-run-card-failure-reason')).not.toBeInTheDocument();
});

test('failed run: renders the failed status and the failure reason (FR-005)', async () => {
	const screen = await render(LintRunCard, {
		run: run({
			status: 'failed',
			completedAt: new Date().toISOString(),
			failureReason: 'Lint agent run showed no liveness for 60 seconds and was terminated.'
		})
	});

	await expect.element(screen.getByTestId('lint-run-card-status')).toHaveTextContent('Failed');
	await expect
		.element(screen.getByTestId('lint-run-card-failure-reason'))
		.toHaveTextContent('no liveness for 60 seconds');
});
