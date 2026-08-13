import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import StatusHistoryPath from './StatusHistoryPath.svelte';
import type { StatusHistoryEntry } from '$lib/types';

// T010 (023-task-ui-improvements, US1 / FR-006, SC-004): the ordered status "path" — every
// recorded entry in order with its timestamp and detail, the last entry highlighted as the
// current one, and a single-entry fallback for tasks recorded before the feature existed.

const failedPath: StatusHistoryEntry[] = [
	{ status: 'received', enteredAt: '2026-08-13T07:00:01Z', detail: null },
	{ status: 'converting', enteredAt: '2026-08-13T07:00:02Z', detail: null },
	{ status: 'queued', enteredAt: '2026-08-13T07:00:05Z', detail: null },
	{ status: 'running', enteredAt: '2026-08-13T07:00:06Z', detail: null },
	{
		status: 'liveness_interrupted',
		enteredAt: '2026-08-13T07:01:06Z',
		detail: 'attempt 1; next retry in 10s'
	},
	{ status: 'failed', enteredAt: '2026-08-13T07:05:00Z', detail: 'Agent run failed.' }
];

test('renders every recorded entry in order', async () => {
	const screen = await render(StatusHistoryPath, {
		entries: failedPath,
		currentStatus: 'failed'
	});

	const entries = screen.getByTestId('status-history-entry').elements();
	expect(entries.map((el) => el.getAttribute('data-status'))).toEqual([
		'received',
		'converting',
		'queued',
		'running',
		'liveness_interrupted',
		'failed'
	]);
});

test('highlights the last entry as the current one — the stopping point of a failure', async () => {
	const screen = await render(StatusHistoryPath, {
		entries: failedPath,
		currentStatus: 'failed'
	});

	const entries = screen.getByTestId('status-history-entry').elements();
	expect(entries.map((el) => el.getAttribute('data-current'))).toEqual([
		'false',
		'false',
		'false',
		'false',
		'false',
		'true'
	]);
	expect(entries.at(-1)?.getAttribute('data-status')).toBe('failed');
});

test('renders the detail text of an entry that carries one', async () => {
	const screen = await render(StatusHistoryPath, {
		entries: failedPath,
		currentStatus: 'failed'
	});

	await expect
		.element(screen.getByTestId('status-history-entry-detail').first())
		.toHaveTextContent('attempt 1; next retry in 10s');
});

test('renders a timestamp for each entry', async () => {
	const screen = await render(StatusHistoryPath, {
		entries: failedPath,
		currentStatus: 'failed'
	});

	const times = screen.getByTestId('status-history-entry').elements();
	for (const entry of times) {
		expect(entry.querySelector('time')).not.toBeNull();
	}
});

test('falls back to the current status as a single entry when no history exists', async () => {
	const screen = await render(StatusHistoryPath, {
		entries: [],
		currentStatus: 'completed'
	});

	const entries = screen.getByTestId('status-history-entry').elements();
	expect(entries).toHaveLength(1);
	expect(entries[0].getAttribute('data-status')).toBe('completed');
	await expect.element(screen.getByTestId('status-history-fallback-note')).toBeVisible();
});

test('does not show the fallback note when real history exists', async () => {
	const screen = await render(StatusHistoryPath, {
		entries: failedPath,
		currentStatus: 'failed'
	});

	await expect.element(screen.getByTestId('status-history-fallback-note')).not.toBeInTheDocument();
});
