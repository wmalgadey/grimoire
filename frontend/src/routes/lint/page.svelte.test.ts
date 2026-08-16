import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import Page from './+page.svelte';

// T026 (013-lint-agent, US1) — the bare trigger button posts, a busy rejection shows a
// clear message, and a completed run's Findings Report renders as formatted markdown
// (mirrors query/page.svelte.test.ts's mocking pattern).

const { triggerLintRunMock, getLintRunMock, getLatestLintRunMock, getLintFindingsMock } =
	vi.hoisted(() => ({
		triggerLintRunMock: vi.fn(),
		getLintRunMock: vi.fn(),
		getLatestLintRunMock: vi.fn(),
		getLintFindingsMock: vi.fn()
	}));

vi.mock('$lib/services/lintApi', async () => {
	const actual =
		await vi.importActual<typeof import('$lib/services/lintApi')>('$lib/services/lintApi');
	return {
		...actual,
		triggerLintRun: (...args: unknown[]) => triggerLintRunMock(...args),
		getLintRun: (...args: unknown[]) => getLintRunMock(...args),
		getLatestLintRun: (...args: unknown[]) => getLatestLintRunMock(...args),
		getLintFindings: (...args: unknown[]) => getLintFindingsMock(...args)
	};
});

test('renders nav links back to the ingest and query UIs', async () => {
	getLatestLintRunMock.mockResolvedValue(null);
	const screen = await render(Page);

	await expect.element(screen.getByTestId('nav-link-ingest')).toHaveAttribute('href', '/');
	await expect.element(screen.getByTestId('nav-link-query')).toHaveAttribute('href', '/query');
});

test('clicking Run Lint triggers a run and shows its running state', async () => {
	getLatestLintRunMock.mockResolvedValue(null);
	triggerLintRunMock.mockReset();
	triggerLintRunMock.mockResolvedValue({
		runId: 'r-1',
		status: 'running',
		triggeredAt: new Date().toISOString()
	});
	getLintRunMock.mockResolvedValue({
		runId: 'r-1',
		status: 'running',
		triggeredAt: new Date().toISOString(),
		completedAt: null,
		failureReason: null,
		hasFindingsReport: false
	});

	const screen = await render(Page);

	await screen.getByTestId('lint-trigger-button').click();

	expect(triggerLintRunMock).toHaveBeenCalledTimes(1);
	await expect.element(screen.getByTestId('lint-run-state')).toHaveTextContent('Running…');
	await expect.element(screen.getByTestId('lint-trigger-button')).toBeDisabled();
});

test('a busy rejection shows a clear message and does not create a run', async () => {
	getLatestLintRunMock.mockResolvedValue(null);
	triggerLintRunMock.mockReset();
	const { LintApiError } = await import('$lib/services/lintApi');
	triggerLintRunMock.mockRejectedValue(
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
		.toHaveTextContent('A Lint Run is already active.');
	// 024 SC-005 (FSI2): rendered through the shared presentation, not this page's own markup.
	await expect.element(screen.getByTestId('lint-trigger-error-details-toggle')).toBeInTheDocument();
	expect(screen.container.querySelector('[data-testid="lint-run-status"]')).toBeNull();
});

test('recovering a completed run on load renders its Findings Report as formatted markdown', async () => {
	getLatestLintRunMock.mockResolvedValue({
		runId: 'r-2',
		status: 'completed',
		triggeredAt: new Date().toISOString(),
		completedAt: new Date().toISOString(),
		failureReason: null,
		hasFindingsReport: true
	});
	getLintFindingsMock.mockResolvedValue({
		runId: 'r-2',
		content: '## Content Quality\n\nNo content-quality findings.\n'
	});

	const screen = await render(Page);

	await expect.element(screen.getByTestId('lint-run-state')).toHaveTextContent('Completed');
	await expect
		.element(screen.getByTestId('lint-findings-report'))
		.toHaveTextContent('No content-quality findings.');
});

test('a failed run shows its failure reason', async () => {
	getLatestLintRunMock.mockResolvedValue({
		runId: 'r-3',
		status: 'failed',
		triggeredAt: new Date().toISOString(),
		completedAt: new Date().toISOString(),
		failureReason: 'Instruction document not found.',
		hasFindingsReport: true
	});
	getLintFindingsMock.mockResolvedValue({ runId: 'r-3', content: 'Run failed before completion.' });

	const screen = await render(Page);

	await expect.element(screen.getByTestId('lint-run-state')).toHaveTextContent('Failed');
	await expect
		.element(screen.getByTestId('lint-run-failure-reason'))
		.toHaveTextContent('Instruction document not found.');
});
