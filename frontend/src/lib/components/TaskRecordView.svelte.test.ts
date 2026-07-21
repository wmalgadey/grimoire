import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import TaskRecordView from './TaskRecordView.svelte';
import type { TaskRecord } from '$lib/types';

// T021 (US2): the detail view renders formatted markdown (not raw source/frontmatter), a
// visually distinct metadata header, and a placeholder for an unavailable record
// (SC-004/SC-006).

function record(overrides: Partial<TaskRecord> = {}): TaskRecord {
	return {
		taskId: 'ingest-98e24a10',
		metadata: {
			status: 'running',
			agent: 'ingest',
			startedAt: '2026-07-18T14:03:11.0000000Z',
			completedAt: null,
			sourceRef: 'raw/sources/ingest-98e24a10.md',
			originalRef: 'raw/originals/ingest-98e24a10.html',
			failureReason: null
		},
		body: '## Stages\n\n- [x] accepted\n- [ ] converted',
		...overrides
	};
}

test('renders markdown as formatted HTML, not raw source or frontmatter', async () => {
	const screen = await render(TaskRecordView, { record: record() });

	const body = screen.getByTestId('task-record-body');
	await expect.element(body).toBeVisible();

	const bodyEl = body.element();
	expect(bodyEl.querySelector('h2')?.textContent).toBe('Stages');

	const bodyText = bodyEl.textContent ?? '';
	expect(bodyText).not.toContain('---');
	expect(bodyText).not.toContain('task_id:');
	expect(bodyEl.innerHTML).not.toContain('##');
});

test('renders a representative full-lifecycle record end to end (SC-006)', async () => {
	const fullLifecycle = record({
		metadata: {
			status: 'completed',
			agent: 'ingest',
			startedAt: '2026-07-18T14:03:11.0000000Z',
			completedAt: '2026-07-18T14:07:22.0000000Z',
			sourceRef: 'raw/sources/ingest-98e24a10.md',
			originalRef: 'raw/originals/ingest-98e24a10.html',
			failureReason: null
		},
		body: [
			'# Ingest Summary',
			'',
			'## Stages',
			'',
			'- [x] accepted',
			'- [x] converting',
			'- [x] queued',
			'- [x] running',
			'- [x] completed',
			'',
			'## Pages',
			'',
			'Some *emphasized* text and a `code span`.',
			'',
			'```',
			'code block content',
			'```'
		].join('\n')
	});

	const screen = await render(TaskRecordView, { record: fullLifecycle });

	const bodyEl = screen.getByTestId('task-record-body').element();
	expect(bodyEl.querySelector('h1')?.textContent).toBe('Ingest Summary');
	expect(bodyEl.querySelector('h2')?.textContent).toBe('Stages');
	expect(bodyEl.querySelector('li')?.textContent).toContain('accepted');
	expect(bodyEl.querySelector('em')?.textContent).toBe('emphasized');
	expect(bodyEl.querySelector('code')?.textContent).toBe('code span');
	expect(bodyEl.querySelector('pre code')?.textContent).toContain('code block content');
});

test('renders the metadata header distinctly from the body, with status/timestamps/refs', async () => {
	const screen = await render(TaskRecordView, { record: record() });

	const metadata = screen.getByTestId('task-record-metadata');
	await expect.element(metadata).toBeVisible();
	await expect.element(screen.getByTestId('task-record-status')).toHaveTextContent('running');
	await expect.element(screen.getByTestId('task-record-agent')).toHaveTextContent('ingest');
	await expect.element(screen.getByTestId('task-record-started-at')).toBeVisible();
	await expect
		.element(screen.getByTestId('task-record-source-ref'))
		.toHaveTextContent('raw/sources/ingest-98e24a10.md');
	await expect
		.element(screen.getByTestId('task-record-original-ref'))
		.toHaveTextContent('raw/originals/ingest-98e24a10.html');
	await expect.element(screen.getByTestId('task-record-completed-at')).not.toBeInTheDocument();
});

test('renders the failure reason when present', async () => {
	const screen = await render(TaskRecordView, {
		record: record({
			metadata: {
				status: 'failed',
				agent: 'ingest',
				startedAt: '2026-07-18T14:03:11.0000000Z',
				completedAt: '2026-07-18T14:04:00.0000000Z',
				sourceRef: null,
				originalRef: null,
				failureReason: 'markitdown conversion timed out after 60s'
			}
		})
	});

	await expect
		.element(screen.getByTestId('task-record-failure-reason'))
		.toHaveTextContent('markitdown conversion timed out after 60s');
});

test('renders the placeholder state for an unavailable record', async () => {
	const screen = await render(TaskRecordView, { record: null });

	await expect.element(screen.getByTestId('task-record-placeholder')).toBeVisible();
	await expect.element(screen.getByTestId('task-record-view')).not.toBeInTheDocument();
});
