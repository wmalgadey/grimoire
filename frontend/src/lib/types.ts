// Shared shapes for the Ingest Intake Web UI, mirroring
// specs/003-ingest-intake-webui/contracts/*.md and data-model.md.

export type IngestSubmissionKind = 'url' | 'markdown_file' | 'pdf_file' | 'office_file';

// Lifecycle stages, end to end (data-model.md TaskArtifact). `received`/`converting` are
// Hub-owned (this feature); `queued -> running -> completed|failed` are agent-owned (001/002).
export type LifecycleStage =
	'received' | 'converting' | 'queued' | 'running' | 'completed' | 'failed';

// 004: named convert step (currently only document-to-Markdown, `markitdown`), and the
// per-submission enabled/disabled map keyed by step name.
export type ConvertStepName = string;
export type ConvertStepConfig = Record<ConvertStepName, boolean>;

export interface ConvertStepDefinition {
	name: ConvertStepName;
	appliesTo: IngestSubmissionKind[];
	requiredFor: IngestSubmissionKind[];
	defaultEnabled: boolean;
}

// 004 (contracts/ingest-submission-api-extension.md): single source of truth for the
// submission form's prompt editor and step toggles.
export interface IngestSubmissionDefaults {
	defaultUserPrompt: string;
	userPromptMaxLength: number;
	convertSteps: ConvertStepDefinition[];
}

export type UserPromptSource = 'default' | 'custom';

export interface SubmissionAcceptedResponse {
	taskId: string;
	status: LifecycleStage;
	sourceKind: IngestSubmissionKind;
	acceptedAt: string;
	userPromptSource?: UserPromptSource;
	convertSteps?: ConvertStepConfig;
}

export interface BoardTask {
	taskId: string;
	status: LifecycleStage;
	title: string;
	updatedAt: string;
	failureReason: string | null;
	taskLink: string;
	queuePosition?: number | null;
}

export interface BoardResponse {
	tasks: BoardTask[];
	queuePaused?: boolean;
}

// 004 (FR-018): live loop-activity snapshot for a running task — loop mechanics only.
export interface RunActivity {
	modelTurns: number;
	toolCalls: number;
	toolCallsByName: Record<string, number>;
	currentAction: string;
	lastEventAt?: string;
}

export interface TaskDetail {
	taskId: string;
	status: LifecycleStage;
	failureReason: string | null;
	sourceRef: string | null;
	originalRef: string | null;
	userPromptSource?: UserPromptSource | null;
	userPrompt?: string | null;
	convertSteps?: ConvertStepConfig | null;
	runActivity?: RunActivity | null;
}

export interface LifecycleEvent {
	eventId: string;
	taskId: string;
	fromStatus: LifecycleStage | null;
	toStatus: LifecycleStage;
	timestamp: string;
	failureReason: string | null;
}

// 004 (contracts/ingest-submission-api-extension.md): realtime `run_activity` payload
// published on the same SignalR channel as lifecycle events.
export interface RunActivityEvent extends RunActivity {
	kind: 'run_activity';
	taskId: string;
}

// 004 (FR-023): client-only projection of the board's SignalR connection lifecycle —
// not a domain entity, purely a display state for ConnectionStatusIndicator.svelte.
export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

// 006 (contracts/task-record-api.md, data-model.md TaskRecord): the per-task markdown
// record's parsed frontmatter, served alongside the frontmatter-stripped markdown body.
export interface TaskRecordMetadata {
	status: string;
	agent: string | null;
	startedAt: string;
	completedAt: string | null;
	sourceRef: string | null;
	originalRef: string | null;
	failureReason: string | null;
}

export interface TaskRecord {
	taskId: string;
	metadata: TaskRecordMetadata;
	body: string;
}

// 006 (contracts/task-record-changed-event.md): realtime notification that a task
// record's file changed; carries no content — consumers refetch the TaskRecord.
export interface TaskRecordChangedEvent {
	eventId: string;
	taskId: string;
	changedAt: string;
}

// 008-query-agent (data-model.md QueryTurn, contracts/query-conversation-api.md).
export type QueryTurnStatus = 'running' | 'completed' | 'interrupted' | 'failed';

/** One prompt-answer exchange, client view (data-model.md Query Turn). */
export interface QueryTurn {
	turnId: string;
	conversationId: string;
	position: number;
	prompt: string;
	answer: string;
	state: QueryTurnStatus;
	failureReason?: string | null;
}

/** POST /api/query-conversations/{conversationId}/turns 202 response. */
export interface QueryTurnAcceptedResponse {
	turnId: string;
	conversationId: string;
	position: number;
	state: QueryTurnStatus;
	acceptedAt: string;
}

/** SignalR `queryAnswerChunk` payload (contracts/query-conversation-api.md). */
export interface QueryAnswerChunkEvent {
	turnId: string;
	sequence: number;
	text: string;
}

/** SignalR `queryTurnChanged` payload (contracts/query-conversation-api.md). */
export interface QueryTurnChangedEvent {
	eventId: string;
	turnId: string;
	fromState: QueryTurnStatus | null;
	toState: QueryTurnStatus;
	timestamp: string;
	failureReason: string | null;
}

// 013-lint-agent (data-model.md "Lint Run"). A bare trigger with no per-run input —
// unlike Query, Lint has no streaming/SignalR channel at all; the client polls
// GET /api/lint-runs/{runId} for status (spec: "at most one run ever active", no
// per-run task board).
export type LintRunStatus = 'running' | 'completed' | 'failed';

/** POST /api/lint-runs 202 response. */
export interface LintRunAcceptedResponse {
	runId: string;
	status: LintRunStatus;
	triggeredAt: string;
}

/** GET /api/lint-runs/{runId} response. */
export interface LintRun {
	runId: string;
	status: LintRunStatus;
	triggeredAt: string;
	completedAt: string | null;
	failureReason: string | null;
	hasFindingsReport: boolean;
}

/** GET /api/lint-runs/{runId}/findings response — the raw Findings Report markdown (contracts/findings-report-format.md). */
export interface LintFindingsReport {
	runId: string;
	content: string;
}

// 015-lint-board-parity (contracts/lint-board-api.md `GET /api/board`, data-model.md
// "Board Entry"): the composite board initial-state response carries all entry kinds,
// explicitly typed via the `kind` discriminator — each kind maps to a distinct card
// component (FR-006). Ingest rows keep exactly their existing field set (FR-015).
export interface IngestTaskBoardEntry extends BoardTask {
	kind: 'ingest_task';
}

export interface LintRunBoardEntry extends LintRun {
	kind: 'lint_run';
}

// data-model.md RemediationActionTask states; cards render from US3 onward, but the
// entry shape is part of the composite board contract from the start.
export type RemediationTaskState =
	'proposed' | 'authorized' | 'executing' | 'completed' | 'failed' | 'not_applicable' | 'dismissed';

export interface RemediationTaskBoardEntry {
	kind: 'remediation_task';
	taskId: string;
	runId: string;
	title: string;
	state: RemediationTaskState;
	proposedAt: string;
	queuePosition: number | null;
	outcomeReason: string | null;
	updatedAt: string;
}

export type BoardEntry = IngestTaskBoardEntry | LintRunBoardEntry | RemediationTaskBoardEntry;

export interface CompositeBoardResponse {
	entries: BoardEntry[];
}

/** SignalR `lintRunLifecycleChanged` payload (contracts/remediation-lifecycle-events.md "Hub 1"). */
export interface LintRunLifecycleEvent {
	eventId: string;
	runId: string;
	fromStatus: LintRunStatus | null;
	toStatus: LintRunStatus;
	timestamp: string;
	failureReason: string | null;
}
