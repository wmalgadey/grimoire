import { describe, expect, test } from 'vitest';
import {
	applyFilters,
	buildBoardItems,
	groupByLane,
	needsYou,
	needsYouSummary,
	toLintItem,
	toRemediationItem,
	type BoardFilters
} from './board';
import type { BoardTask, LintRun, RemediationTaskBoardEntry } from './types';

// The board's merge is the one piece of the redesign with real logic in it: a lint run and a
// remediation proposal have to land in the right ingest lane, keep the status word that adds
// information, and drop the one that only repeats the lane heading. It is a pure projection,
// so it is tested here rather than through the rendered board.

function task(overrides: Partial<BoardTask> = {}): BoardTask {
	return {
		taskId: 'ing-1',
		status: 'received',
		title: 'A source',
		updatedAt: '2026-08-16T14:00:00Z',
		failureReason: null,
		taskLink: '/api/ingest-submissions/ing-1',
		...overrides
	};
}

function lintRun(overrides: Partial<LintRun> = {}): LintRun {
	return {
		runId: 'lint-24',
		status: 'completed',
		triggeredAt: '2026-08-16T13:35:00Z',
		completedAt: '2026-08-16T13:40:00Z',
		failureReason: null,
		hasFindingsReport: true,
		...overrides
	};
}

function remediation(
	overrides: Partial<RemediationTaskBoardEntry> = {}
): RemediationTaskBoardEntry {
	return {
		kind: 'remediation_task',
		taskId: 'rem-1',
		runId: 'lint-24',
		title: 'Reconcile the retention window',
		state: 'proposed',
		proposedAt: '2026-08-16T13:41:00Z',
		queuePosition: null,
		outcomeReason: null,
		updatedAt: '2026-08-16T13:41:00Z',
		...overrides
	};
}

describe('lane placement', () => {
	test('an ingest task keeps its own stage as its lane', () => {
		const items = buildBoardItems({
			tasks: [task({ status: 'converting' })],
			lintRun: null,
			remediationTasks: []
		});

		expect(items[0].lane).toBe('converting');
	});

	test('a lint run lands in the lane matching its status, tagged as a health check', () => {
		expect(toLintItem(lintRun({ status: 'running' })).lane).toBe('running');
		expect(toLintItem(lintRun({ status: 'completed' })).lane).toBe('completed');
		expect(toLintItem(lintRun({ status: 'failed' })).lane).toBe('failed');
		expect(toLintItem(lintRun()).tagLabel).toBe('Health check');
	});

	test.each([
		['proposed', 'received'],
		['authorized', 'queued'],
		['executing', 'running'],
		['completed', 'completed'],
		['not_applicable', 'completed'],
		['dismissed', 'completed'],
		['failed', 'failed']
	] as const)('a %s remediation task sits in the %s lane', (state, lane) => {
		expect(toRemediationItem(remediation({ state })).lane).toBe(lane);
	});

	test('a remediation tag is kept when it adds a word, dropped when it repeats the lane', () => {
		// "Proposed" in Received says something the lane does not; "Completed" in Done does not.
		expect(toRemediationItem(remediation({ state: 'proposed' })).tagLabel).toBe('Proposed');
		expect(toRemediationItem(remediation({ state: 'completed' })).tagLabel).toBeNull();
	});

	test('a not_applicable outcome reason is not presented as a failure', () => {
		const item = toRemediationItem(
			remediation({ state: 'not_applicable', outcomeReason: 'Nothing to change.' })
		);

		expect(item.failureReason).toBeNull();
	});

	test('a failed remediation outcome reason is the card failure reason', () => {
		const item = toRemediationItem(
			remediation({ state: 'failed', outcomeReason: 'Guardrail denied write' })
		);

		expect(item.failureReason).toBe('Guardrail denied write');
	});

	test('a running ingest task carries its live loop activity, other stages do not', () => {
		const activity = {
			modelTurns: 7,
			toolCalls: 21,
			toolCallsByName: { read_wiki: 9 },
			currentAction: 'writing wiki page'
		};
		const items = buildBoardItems({
			tasks: [
				task({ taskId: 'ing-run', status: 'running' }),
				task({ taskId: 'ing-done', status: 'completed' })
			],
			lintRun: null,
			remediationTasks: [],
			runActivityByTaskId: { 'ing-run': activity, 'ing-done': activity }
		});

		expect(items[0].runActivity).toEqual(activity);
		expect(items[0].note).toBe('7 turns · 21 tool calls');
		expect(items[1].runActivity).toBeNull();
	});

	test('groups every kind into one set of lanes', () => {
		const lanes = groupByLane(
			buildBoardItems({
				tasks: [task({ status: 'failed' })],
				lintRun: lintRun({ status: 'completed' }),
				remediationTasks: [remediation({ state: 'proposed' })]
			})
		);

		expect(lanes.failed.map((i) => i.kind)).toEqual(['ingest']);
		expect(lanes.completed.map((i) => i.kind)).toEqual(['lint']);
		expect(lanes.received.map((i) => i.kind)).toEqual(['remediation']);
	});
});

describe('filters', () => {
	const items = buildBoardItems({
		tasks: [
			task({
				taskId: 'ing-old',
				title: 'Vendor SOW.pdf',
				status: 'completed',
				updatedAt: '2026-08-10T09:00:00Z'
			}),
			task({ taskId: 'ing-bad', title: 'Broken source', status: 'failed' })
		],
		lintRun: lintRun(),
		remediationTasks: [remediation()]
	});
	const base: BoardFilters = {
		query: '',
		failedOnly: false,
		lastDayOnly: false,
		needsYouOnly: false
	};
	const now = new Date('2026-08-16T15:00:00Z').getTime();

	test('the search matches title and id alike', () => {
		expect(applyFilters(items, { ...base, query: 'vendor' }, now).map((i) => i.id)).toEqual([
			'ing-old'
		]);
		expect(applyFilters(items, { ...base, query: 'lint-24' }, now).map((i) => i.id)).toEqual([
			'lint-24'
		]);
	});

	test('failed only keeps the failed lane, whatever kind it is', () => {
		expect(applyFilters(items, { ...base, failedOnly: true }, now).map((i) => i.id)).toEqual([
			'ing-bad'
		]);
	});

	test('last 24h drops what has not moved since', () => {
		expect(applyFilters(items, { ...base, lastDayOnly: true }, now).map((i) => i.id)).not.toContain(
			'ing-old'
		);
	});

	test('needs-you keeps proposals awaiting authorization and failures', () => {
		expect(
			applyFilters(items, { ...base, needsYouOnly: true }, now)
				.map((i) => i.id)
				.sort()
		).toEqual(['ing-bad', 'rem-1']);
	});

	test('needsYou counts a proposal and a failure, not a finished task', () => {
		expect(
			items
				.filter(needsYou)
				.map((i) => i.id)
				.sort()
		).toEqual(['ing-bad', 'rem-1']);
	});

	test('the triage summary states only what is true', () => {
		expect(needsYouSummary(items, true)).toBe(
			'1 remediation proposal · 1 failed task · queue paused'
		);
		expect(needsYouSummary([], false)).toBe('');
	});
});
