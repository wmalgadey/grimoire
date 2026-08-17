import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import LintTriggerPopover from './LintTriggerPopover.svelte';
import { LintApiError } from '$lib/services/lintApi';

// Ports T019 (015-lint-board-parity, US2) from the board's inline button onto the nav's
// popover: a lint run is still started in one action (SC-003/FR-002), and both blocked-trigger
// reasons are still surfaced as clear, user-facing messages, never silence (FR-004/SC-004).
// What the run does next is unchanged — it appears on the board through the lint stream.

const { triggerMock } = vi.hoisted(() => ({ triggerMock: vi.fn() }));

vi.mock('$lib/services/lintApi', async (importOriginal) => {
	const actual = await importOriginal<typeof import('$lib/services/lintApi')>();
	return { ...actual, triggerLintRun: triggerMock };
});

beforeEach(() => {
	triggerMock.mockReset();
	triggerMock.mockResolvedValue({
		runId: '2026-08-16-lint-1',
		status: 'running',
		triggeredAt: new Date().toISOString()
	});
});

test('accepted trigger: pick a model, run, and the popover closes', async () => {
	const screen = await render(LintTriggerPopover);

	await screen.getByTestId('nav-lint-button').click();
	await screen.getByTestId('lint-trigger-button').click();

	expect(triggerMock).toHaveBeenCalledOnce();
	await expect.element(screen.getByTestId('lint-popover')).not.toBeInTheDocument();
});

test('blocked trigger (lint_run_active): the reason is shown, never a silent no-op', async () => {
	triggerMock.mockRejectedValue(
		new LintApiError(
			'A Lint Run is already active. Wait for it to finish before triggering another.',
			409,
			'lint_run_active'
		)
	);
	const screen = await render(LintTriggerPopover);

	await screen.getByTestId('nav-lint-button').click();
	await screen.getByTestId('lint-trigger-button').click();

	await expect
		.element(screen.getByTestId('lint-trigger-error'))
		.toHaveTextContent('A Lint Run is already active');
	// 024 SC-005 (FSI2): it renders through the shared presentation — the disclosure toggle is
	// the observable part only that component has.
	await expect.element(screen.getByTestId('lint-trigger-error-details-toggle')).toBeInTheDocument();
	// The popover stays open, so the reason is still on screen next to the button that failed.
	await expect.element(screen.getByTestId('lint-popover')).toBeVisible();
});

test('blocked trigger (unresolved_remediation_tasks): the reason is shown too', async () => {
	triggerMock.mockRejectedValue(
		new LintApiError(
			'Remediation action tasks from the previous lint run are still unresolved. Authorize, dismiss, or wait for them to finish before starting a new run.',
			409,
			'unresolved_remediation_tasks'
		)
	);
	const screen = await render(LintTriggerPopover);

	await screen.getByTestId('nav-lint-button').click();
	await screen.getByTestId('lint-trigger-button').click();

	await expect
		.element(screen.getByTestId('lint-trigger-error'))
		.toHaveTextContent('Remediation action tasks from the previous lint run are still unresolved');
});

test('the model choice defaults to Opus for lint and is remembered while the popover is open', async () => {
	const screen = await render(LintTriggerPopover);

	await screen.getByTestId('nav-lint-button').click();
	await expect
		.element(screen.getByTestId('model-option').filter({ hasText: 'Claude Opus 4.1' }))
		.toHaveAttribute('aria-pressed', 'true');

	await screen.getByTestId('model-option').filter({ hasText: 'Claude Haiku 4.5' }).click();

	await expect
		.element(screen.getByTestId('model-option').filter({ hasText: 'Claude Haiku 4.5' }))
		.toHaveAttribute('aria-pressed', 'true');
});

test('cancel and the backdrop both dismiss without triggering a run', async () => {
	const screen = await render(LintTriggerPopover);

	await screen.getByTestId('nav-lint-button').click();
	await screen.getByTestId('lint-popover-cancel').click();
	await expect.element(screen.getByTestId('lint-popover')).not.toBeInTheDocument();

	await screen.getByTestId('nav-lint-button').click();
	// Dispatched rather than driven through the pointer: the backdrop is sized by a Tailwind
	// utility, and component tests render without that sheet.
	(screen.getByTestId('lint-popover-backdrop').element() as HTMLElement).click();
	await expect.element(screen.getByTestId('lint-popover')).not.toBeInTheDocument();

	expect(triggerMock).not.toHaveBeenCalled();
});

test('dismissing after a failure clears it, so reopening starts clean', async () => {
	triggerMock.mockRejectedValue(
		new LintApiError(
			'A Lint Run is already active. Wait for it to finish before triggering another.',
			409,
			'lint_run_active'
		)
	);
	const screen = await render(LintTriggerPopover);

	await screen.getByTestId('nav-lint-button').click();
	await screen.getByTestId('lint-trigger-button').click();
	await expect.element(screen.getByTestId('lint-trigger-error')).toBeVisible();

	await screen.getByTestId('lint-popover-cancel').click();
	await screen.getByTestId('nav-lint-button').click();

	// The operator is starting a fresh attempt; the previous one's reason is not theirs to
	// read again, and leaving it up would describe a state that may no longer hold.
	await expect.element(screen.getByTestId('lint-popover')).toBeVisible();
	await expect.element(screen.getByTestId('lint-trigger-error')).not.toBeInTheDocument();
});
