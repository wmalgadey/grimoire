import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import BoardCard from './BoardCard.svelte';
import { toIngestItem, toLintItem, toRemediationItem } from '$lib/board';
import type { BoardTask } from '$lib/types';

// One card for every kind of work (this replaces TaskCard, LintRunCard and
// RemediationTaskCard). It carries the label, the raw id (023 FR-003/FR-004) and one line of
// context — and nothing actionable: every action moved into the popover it opens.

function ingest(overrides: Partial<BoardTask> = {}) {
	return toIngestItem({
		taskId: 'ing-1',
		status: 'received',
		title: 'Retention policy draft.docx',
		updatedAt: '2026-08-16T14:02:00Z',
		failureReason: null,
		taskLink: '/api/ingest-submissions/ing-1',
		...overrides
	});
}

test('shows the human label as the card text with the raw id beneath it', async () => {
	const screen = await render(BoardCard, { item: ingest(), onOpen: () => {} });

	await expect
		.element(screen.getByTestId('board-card-title'))
		.toHaveTextContent('Retention policy draft.docx');
	await expect.element(screen.getByTestId('board-card-id')).toHaveTextContent('ing-1');
});

// #130: when the label chain falls all the way through, the title *is* the task id, and the
// card printed that same string twice — which reads as broken rather than as "not known yet".
test('prints the id once when it is also the label', async () => {
	const screen = await render(BoardCard, {
		item: ingest({ title: 'ing-1' }),
		onOpen: () => {}
	});

	await expect.element(screen.getByTestId('board-card-title')).toHaveTextContent('ing-1');
	expect(screen.getByTestId('board-card-id').elements()).toHaveLength(0);
});

test('a queued card states its position, a running card its loop activity', async () => {
	const queued = await render(BoardCard, {
		item: ingest({ status: 'queued', queuePosition: 2 }),
		onOpen: () => {}
	});
	await expect.element(queued.getByTestId('board-card-note')).toHaveTextContent('#2 in queue');
	queued.unmount();

	const running = await render(BoardCard, {
		item: toIngestItem(
			{
				taskId: 'ing-2',
				status: 'running',
				title: 'ACME quarterly report.pdf',
				updatedAt: '2026-08-16T13:58:00Z',
				failureReason: null,
				taskLink: '/api/ingest-submissions/ing-2'
			},
			{ modelTurns: 7, toolCalls: 21, toolCallsByName: {}, currentAction: 'writing wiki page' }
		),
		onOpen: () => {}
	});
	await expect
		.element(running.getByTestId('board-card-note'))
		.toHaveTextContent('7 turns · 21 tool calls');
	// Presence, not visibility: component tests render without the app's Tailwind sheet, so a
	// utility-sized dot has no box to measure.
	await expect.element(running.getByTestId('board-card-live-dot')).toBeInTheDocument();
});

// 024 FR-012: what the harness recorded is untouched — the card shows the readable sentence
// of it, and the full text with its technical detail stays in the popover.
test('a failed card shows the recorded reason', async () => {
	const screen = await render(BoardCard, {
		item: ingest({ status: 'failed', failureReason: 'Fetch failed: 403 Forbidden' }),
		onOpen: () => {}
	});

	await expect
		.element(screen.getByTestId('board-card-failure-reason'))
		.toHaveTextContent('Fetch failed: 403 Forbidden');
});

test('a lint run and a remediation proposal read as themselves on the same card shape', async () => {
	const lint = await render(BoardCard, {
		item: toLintItem({
			runId: 'lint-24',
			status: 'completed',
			triggeredAt: '2026-08-16T13:35:00Z',
			completedAt: '2026-08-16T13:40:00Z',
			failureReason: null,
			hasFindingsReport: true
		}),
		onOpen: () => {}
	});
	await expect.element(lint.getByTestId('board-card-title')).toHaveTextContent('Wiki health check');
	await expect.element(lint.getByTestId('board-card-tag')).toHaveTextContent('Health check');
	lint.unmount();

	const remediation = await render(BoardCard, {
		item: toRemediationItem({
			kind: 'remediation_task',
			taskId: 'rem-1',
			runId: 'lint-24',
			title: 'Reconcile the retention window',
			state: 'proposed',
			proposedAt: '2026-08-16T13:41:00Z',
			queuePosition: null,
			outcomeReason: null,
			updatedAt: '2026-08-16T13:41:00Z'
		}),
		onOpen: () => {}
	});
	await expect.element(remediation.getByTestId('board-card-tag')).toHaveTextContent('Proposed');
	await expect
		.element(remediation.getByTestId('board-card-note'))
		.toHaveTextContent('awaits authorization');
});

test('clicking the card hands the item and its own element to the board', async () => {
	const onOpen = vi.fn();
	const screen = await render(BoardCard, { item: ingest(), onOpen });

	await screen.getByTestId('board-card').click();

	expect(onOpen).toHaveBeenCalledOnce();
	const [item, anchor] = onOpen.mock.calls[0];
	expect(item.id).toBe('ing-1');
	expect((anchor as HTMLElement).dataset.testid).toBe('board-card');
});
