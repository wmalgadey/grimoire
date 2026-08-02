import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import { LintApiError } from '$lib/services/lintApi';
import type { LintRun, LintRunAcceptedResponse } from '$lib/types';

// T019 (015-lint-board-parity, US2): the board's lint trigger — a single action from the
// board route (SC-003/FR-002) — and both blocked-trigger reasons surfaced as clear,
// user-facing messages, never silence (FR-004/SC-004). Named per the project's
// `*.svelte.test.ts` convention so it runs in the browser (client) vitest project.

const { triggerMock, onRunChangedHandlers } = vi.hoisted(() => ({
	triggerMock: vi.fn<() => Promise<LintRunAcceptedResponse>>(),
	onRunChangedHandlers: [] as Array<(run: LintRun | null) => void>
}));

// Same isolation idiom as page.svelte.test.ts: the streams are mocked so no SignalR/
// fetch traffic leaves the test.
vi.mock('$lib/services/ingestLifecycleClient', () => ({
	createBoardLifecycleStream: () => ({
		start: async () => {},
		stop: async () => {}
	})
}));

vi.mock('$lib/services/lintLifecycleClient', () => ({
	createLintRunStream: (onRunChanged: (run: LintRun | null) => void) => {
		onRunChangedHandlers.push(onRunChanged);
		return {
			start: async () => {},
			stop: async () => {}
		};
	}
}));

vi.mock('$lib/services/lintApi', async (importOriginal) => {
	const actual = await importOriginal<typeof import('$lib/services/lintApi')>();
	return { ...actual, triggerLintRun: triggerMock };
});

test('accepted trigger: one click on the board starts a run and the lint card shows it running', async () => {
	triggerMock.mockResolvedValueOnce({
		runId: '2026-08-01-lint-trigger1',
		status: 'running',
		triggeredAt: new Date().toISOString()
	});

	const screen = await render(Page);
	await screen.getByTestId('lint-trigger-button').click();

	expect(triggerMock).toHaveBeenCalledOnce();
	const status = screen.getByTestId('lint-run-card-status');
	await expect.element(status).toHaveAttribute('data-status', 'running');
	await expect.element(screen.getByTestId('lint-trigger-error')).not.toBeInTheDocument();
});

test('blocked trigger (lint_run_active): the reason is shown, never a silent no-op', async () => {
	triggerMock.mockRejectedValueOnce(
		new LintApiError(
			'A Lint Run is already active. Wait for it to finish before triggering another.',
			409,
			'lint_run_active'
		)
	);

	const screen = await render(Page);
	await screen.getByTestId('lint-trigger-button').click();

	await expect
		.element(screen.getByTestId('lint-trigger-error'))
		.toHaveTextContent('A Lint Run is already active');
});

test('blocked trigger (unresolved_remediation_tasks): the reason is shown, never a silent no-op', async () => {
	triggerMock.mockRejectedValueOnce(
		new LintApiError(
			'Remediation action tasks from the previous lint run are still unresolved. Authorize, dismiss, or wait for them to finish before starting a new run.',
			409,
			'unresolved_remediation_tasks'
		)
	);

	const screen = await render(Page);
	await screen.getByTestId('lint-trigger-button').click();

	await expect
		.element(screen.getByTestId('lint-trigger-error'))
		.toHaveTextContent('Remediation action tasks from the previous lint run are still unresolved');
});

test('live lint stream updates still drive the card after a trigger error is dismissed by a new run', async () => {
	// The stream stays authoritative: a run appearing via the lifecycle stream (e.g.
	// triggered from the /lint page) renders on the board card (spec edge case, SC-001).
	onRunChangedHandlers.length = 0;
	const screen = await render(Page);
	const onRunChanged = onRunChangedHandlers.at(-1);
	if (!onRunChanged) throw new Error('createLintRunStream was never started');

	onRunChanged({
		runId: '2026-08-01-lint-elsewhere',
		status: 'completed',
		triggeredAt: new Date().toISOString(),
		completedAt: new Date().toISOString(),
		failureReason: null,
		hasFindingsReport: true
	});

	await expect
		.element(screen.getByTestId('lint-run-card-status'))
		.toHaveAttribute('data-status', 'completed');
});
