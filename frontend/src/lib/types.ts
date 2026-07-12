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
