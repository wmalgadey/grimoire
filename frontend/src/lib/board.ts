/**
 * The board's view model — one lane-placed item per board entry, whatever kind it is.
 *
 * The Hi-Fi design (Grimoire Hi-Fi, board 4a/4c) drops the separate "Wiki health check"
 * section that used to sit above the columns: a lint run and an agent-proposed remediation
 * are ordinary work items and belong in the same lanes as an ingest, distinguished by their
 * text and their tag rather than by living in their own strip. The three live streams stay
 * exactly as they are (independent hubs, 015 FR-015) — this module is the pure projection
 * that merges their snapshots into one list the lanes can render, so the merge itself is
 * testable without a browser.
 */

import type {
	BoardTask,
	LifecycleStage,
	LintRun,
	RemediationTaskBoardEntry,
	RemediationTaskState,
	RunActivity
} from './types';

export type BoardItemKind = 'ingest' | 'lint' | 'remediation';

export interface BoardItem {
	/** Unique across kinds — two kinds could in principle carry the same id string. */
	key: string;
	kind: BoardItemKind;
	/** The task id / run id, shown verbatim under the title (023 FR-004). */
	id: string;
	title: string;
	/** The lane this item is drawn in. */
	lane: LifecycleStage;
	/**
	 * The item's own status word, or null when it would only repeat the lane's name — the
	 * design drops the tag in that case and keeps just the time (chat 3: "Stage tags are gone
	 * from cards where they repeat the column name").
	 */
	tagLabel: string | null;
	/** One line of context under the id: queue position, live loop activity, provenance. */
	note: string | null;
	failureReason: string | null;
	updatedAt: string;
	/** Present for the kinds that have a detail route; null for a lint run. */
	detailTaskId: string | null;
	queuePosition: number | null;
	/** Live loop activity for a running ingest task (004 FR-018); null otherwise. */
	runActivity: RunActivity | null;
	/** Set for a remediation entry, so the popover can offer the right review action. */
	remediationState: RemediationTaskState | null;
	/** The lint run a remediation proposal came out of; null for other kinds. */
	sourceRunId: string | null;
	/** Set for a lint run, so the popover can offer its Findings Report. */
	lintRunId: string | null;
	lintHasFindingsReport: boolean;
}

const STAGE_LABELS: Record<LifecycleStage, string> = {
	received: 'Received',
	converting: 'Converting',
	queued: 'Queued',
	running: 'Running',
	completed: 'Completed',
	failed: 'Failed'
};

/** Lane headings — "Done" rather than "Completed", per the design's lane strip. */
export const LANE_LABELS: Record<LifecycleStage, string> = {
	received: 'Received',
	converting: 'Convert',
	queued: 'Queued',
	running: 'Running',
	completed: 'Done',
	failed: 'Failed'
};

export const LANES: readonly LifecycleStage[] = [
	'received',
	'converting',
	'queued',
	'running',
	'completed',
	'failed'
];

const REMEDIATION_LABELS: Record<RemediationTaskState, string> = {
	proposed: 'Proposed',
	authorized: 'Waiting',
	executing: 'Executing',
	completed: 'Completed',
	failed: 'Failed',
	not_applicable: 'Not applicable',
	dismissed: 'Dismissed'
};

/**
 * Where a remediation task sits among the ingest lanes. `proposed` lands in Received — it is
 * the newest thing on the board and the one waiting on a person — while the terminal
 * non-failure states collect in Done.
 */
const REMEDIATION_LANES: Record<RemediationTaskState, LifecycleStage> = {
	proposed: 'received',
	authorized: 'queued',
	executing: 'running',
	completed: 'completed',
	not_applicable: 'completed',
	dismissed: 'completed',
	failed: 'failed'
};

const LINT_LANES: Record<LintRun['status'], LifecycleStage> = {
	running: 'running',
	completed: 'completed',
	failed: 'failed'
};

/** The tag is dropped when it would only repeat the lane heading it sits in. */
function tagFor(label: string, lane: LifecycleStage): string | null {
	return label === STAGE_LABELS[lane] ? null : label;
}

function ingestNote(task: BoardTask, activity: RunActivity | null): string | null {
	if (task.status === 'queued' && task.queuePosition != null) {
		return `#${task.queuePosition} in queue`;
	}
	if (task.status === 'running' && activity) {
		return `${activity.modelTurns} turns · ${activity.toolCalls} tool calls`;
	}
	return null;
}

export function toIngestItem(task: BoardTask, activity: RunActivity | null = null): BoardItem {
	return {
		key: `ingest:${task.taskId}`,
		kind: 'ingest',
		id: task.taskId,
		title: task.title,
		lane: task.status,
		tagLabel: null,
		note: ingestNote(task, activity),
		failureReason: task.failureReason,
		updatedAt: task.updatedAt,
		detailTaskId: task.taskId,
		queuePosition: task.queuePosition ?? null,
		runActivity: task.status === 'running' ? activity : null,
		remediationState: null,
		sourceRunId: null,
		lintRunId: null,
		lintHasFindingsReport: false
	};
}

export function toLintItem(run: LintRun): BoardItem {
	const lane = LINT_LANES[run.status];
	return {
		key: `lint:${run.runId}`,
		kind: 'lint',
		id: run.runId,
		title: 'Wiki health check',
		lane,
		tagLabel: tagFor('Health check', lane),
		note: null,
		failureReason: run.failureReason,
		updatedAt: run.completedAt ?? run.triggeredAt,
		detailTaskId: null,
		queuePosition: null,
		runActivity: null,
		remediationState: null,
		sourceRunId: null,
		lintRunId: run.runId,
		lintHasFindingsReport: run.hasFindingsReport
	};
}

export function toRemediationItem(entry: RemediationTaskBoardEntry): BoardItem {
	const lane = REMEDIATION_LANES[entry.state];
	return {
		key: `remediation:${entry.taskId}`,
		kind: 'remediation',
		id: entry.taskId,
		title: entry.title,
		lane,
		tagLabel: tagFor(REMEDIATION_LABELS[entry.state], lane),
		note:
			entry.state === 'proposed'
				? `from ${entry.runId} — awaits authorization`
				: `from ${entry.runId}`,
		// A `not_applicable` outcome is not a failure — the agent looked and found nothing to do
		// (015 FR-005/FR-018) — so its reason stays out of the card's failure slot.
		failureReason: entry.state === 'failed' ? entry.outcomeReason : null,
		updatedAt: entry.updatedAt,
		detailTaskId: entry.taskId,
		queuePosition: entry.queuePosition,
		runActivity: null,
		remediationState: entry.state,
		sourceRunId: entry.runId,
		lintRunId: null,
		lintHasFindingsReport: false
	};
}

export function buildBoardItems(input: {
	tasks: BoardTask[];
	lintRun: LintRun | null;
	remediationTasks: RemediationTaskBoardEntry[];
	runActivityByTaskId?: Record<string, RunActivity>;
}): BoardItem[] {
	const activity = input.runActivityByTaskId ?? {};
	return [
		...input.tasks.map((task) => toIngestItem(task, activity[task.taskId] ?? null)),
		...(input.lintRun ? [toLintItem(input.lintRun)] : []),
		...input.remediationTasks.map(toRemediationItem)
	];
}

export interface BoardFilters {
	/** Free text over title and id. */
	query: string;
	failedOnly: boolean;
	lastDayOnly: boolean;
	/** The triage strip's "show only these" — proposals awaiting you, plus failures. */
	needsYouOnly: boolean;
}

export const EMPTY_FILTERS: BoardFilters = {
	query: '',
	failedOnly: false,
	lastDayOnly: false,
	needsYouOnly: false
};

/** What the triage strip counts: everything that is waiting on a person. */
export function needsYou(item: BoardItem): boolean {
	return item.remediationState === 'proposed' || item.lane === 'failed';
}

const ONE_DAY_MS = 24 * 60 * 60 * 1000;

export function applyFilters(
	items: BoardItem[],
	filters: BoardFilters,
	now: number = Date.now()
): BoardItem[] {
	const query = filters.query.trim().toLowerCase();
	return items.filter((item) => {
		if (query && !`${item.title} ${item.id}`.toLowerCase().includes(query)) return false;
		if (filters.failedOnly && item.lane !== 'failed') return false;
		if (filters.needsYouOnly && !needsYou(item)) return false;
		if (filters.lastDayOnly) {
			const updated = new Date(item.updatedAt).getTime();
			// An unparseable timestamp is kept rather than silently dropped: hiding a task
			// because its clock reading was odd is the worse of the two failure modes.
			if (!Number.isNaN(updated) && now - updated > ONE_DAY_MS) return false;
		}
		return true;
	});
}

export function groupByLane(items: BoardItem[]): Record<LifecycleStage, BoardItem[]> {
	const grouped = Object.fromEntries(LANES.map((lane) => [lane, [] as BoardItem[]])) as Record<
		LifecycleStage,
		BoardItem[]
	>;
	for (const item of items) grouped[item.lane].push(item);
	return grouped;
}

/** The triage strip's sentence — only the parts that are actually true right now. */
export function needsYouSummary(items: BoardItem[], queuePaused: boolean): string {
	const proposals = items.filter((i) => i.remediationState === 'proposed').length;
	const failed = items.filter((i) => i.lane === 'failed').length;
	const parts: string[] = [];
	if (proposals > 0) parts.push(`${proposals} remediation proposal${proposals === 1 ? '' : 's'}`);
	if (failed > 0) parts.push(`${failed} failed task${failed === 1 ? '' : 's'}`);
	if (queuePaused) parts.push('queue paused');
	return parts.join(' · ');
}
