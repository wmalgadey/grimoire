// Shared shapes for the Ingest Intake Web UI, mirroring
// specs/003-ingest-intake-webui/contracts/*.md and data-model.md.

export type IngestSubmissionKind = 'url' | 'markdown_file' | 'pdf_file' | 'office_file';

// Lifecycle stages, end to end (data-model.md TaskArtifact). `received`/`converting` are
// Hub-owned (this feature); `queued -> running -> completed|failed` are agent-owned (001/002).
export type LifecycleStage =
	'received' | 'converting' | 'queued' | 'running' | 'completed' | 'failed';

export interface SubmissionAcceptedResponse {
	taskId: string;
	status: LifecycleStage;
	sourceKind: IngestSubmissionKind;
	acceptedAt: string;
}

export interface BoardTask {
	taskId: string;
	status: LifecycleStage;
	title: string;
	updatedAt: string;
	failureReason: string | null;
	taskLink: string;
}

export interface TaskDetail {
	taskId: string;
	status: LifecycleStage;
	failureReason: string | null;
	sourceRef: string | null;
	originalRef: string | null;
}

export interface LifecycleEvent {
	eventId: string;
	taskId: string;
	fromStatus: LifecycleStage | null;
	toStatus: LifecycleStage;
	timestamp: string;
	failureReason: string | null;
}
