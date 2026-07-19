import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import Page from './+page.svelte';
import type { TaskRecord } from '$lib/types';

// T029 (US2): the detail route reads taskId from route params, fetches via
// getTaskRecord, and renders TaskRecordView — including the placeholder path for an
// unavailable record (404).

const { getTaskRecordMock } = vi.hoisted(() => ({
	getTaskRecordMock: vi.fn()
}));

vi.mock('$lib/services/ingestSubmissionsApi', () => ({
	getTaskRecord: getTaskRecordMock
}));

function record(overrides: Partial<TaskRecord> = {}): TaskRecord {
	return {
		taskId: 'task-1',
		metadata: {
			status: 'completed',
			agent: 'ingest',
			startedAt: '2026-07-18T14:03:11.0000000Z',
			completedAt: '2026-07-18T14:05:00.0000000Z',
			sourceRef: 'raw/sources/task-1.md',
			originalRef: null,
			failureReason: null
		},
		body: '# Heading\n\nBody text.',
		...overrides
	};
}

test('reads taskId from route params and renders the fetched record', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'ok', record: record() });

	const screen = await render(Page, { data: { taskId: 'task-1' }, params: { taskId: 'task-1' } });

	await expect.element(screen.getByTestId('task-record-page-title')).toHaveTextContent('task-1');
	await expect.element(screen.getByTestId('task-record-view')).toBeVisible();
	expect(getTaskRecordMock).toHaveBeenCalledWith('task-1');
});

test('renders the placeholder when the record is unavailable', async () => {
	getTaskRecordMock.mockResolvedValue({ status: 'unavailable' });

	const screen = await render(Page, {
		data: { taskId: 'missing-task' },
		params: { taskId: 'missing-task' }
	});

	await expect.element(screen.getByTestId('task-record-placeholder')).toBeVisible();
	await expect.element(screen.getByTestId('task-record-view')).not.toBeInTheDocument();
});
