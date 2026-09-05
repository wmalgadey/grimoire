import type {
	BoardResponse,
	BoardTask,
	ConvertStepConfig,
	IngestSubmissionDefaults,
	IngestSubmissionKind,
	SubmissionAcceptedResponse,
	TaskDetail,
	TaskRecord
} from '$lib/types';
import { presentResponseError, type PresentedError } from './apiError';

const BASE_PATH = '/api/ingest-submissions';
const QUEUE_BASE_PATH = '/api/ingest-queue';

export class IngestSubmissionApiError extends Error {
	constructor(
		message: string,
		public readonly status: number,
		public readonly code?: string,
		/** The shared presentation of this failure; every surface renders this, not `message`. */
		public readonly presented?: PresentedError
	) {
		super(message);
		this.name = 'IngestSubmissionApiError';
	}
}

/**
 * 024 (ADR-026): replaces httpErrorMessage.ts, whose fallback branch displayed the Hub's raw
 * machine identifier to the user when a response carried no prose — the literal defect issue #85
 * reported. There is no such branch any more: the Hub always sends a sentence, and where it cannot
 * be read the category supplies one.
 */
async function toApiError(response: Response): Promise<IngestSubmissionApiError> {
	const presented = await presentResponseError(response);
	return new IngestSubmissionApiError(
		presented.message,
		response.status,
		presented.code ?? undefined,
		presented
	);
}

// 004: optional per-submission steering prompt and convert-step overrides (FR-006, FR-011).
// Both stay optional so a caller that doesn't touch either reproduces feature 003 exactly.
export interface SubmissionOptions {
	userPrompt?: string;
	convertSteps?: ConvertStepConfig;
	fetchImpl?: typeof fetch;
}

export async function submitIngestUrl(
	url: string,
	options: SubmissionOptions = {}
): Promise<SubmissionAcceptedResponse> {
	const fetchImpl = options.fetchImpl ?? fetch;
	const response = await fetchImpl(BASE_PATH, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({
			kind: 'url',
			url,
			...(options.userPrompt ? { userPrompt: options.userPrompt } : {}),
			...(options.convertSteps ? { convertSteps: options.convertSteps } : {})
		})
	});

	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}

export async function submitIngestFile(
	kind: Exclude<IngestSubmissionKind, 'url'>,
	file: File,
	options: SubmissionOptions = {}
): Promise<SubmissionAcceptedResponse> {
	const fetchImpl = options.fetchImpl ?? fetch;
	const formData = new FormData();
	formData.set('kind', kind);
	formData.set('file', file);
	if (options.userPrompt) formData.set('userPrompt', options.userPrompt);
	if (options.convertSteps) formData.set('convertSteps', JSON.stringify(options.convertSteps));

	const response = await fetchImpl(BASE_PATH, { method: 'POST', body: formData });

	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}

export async function listIngestBoard(fetchImpl: typeof fetch = fetch): Promise<BoardTask[]> {
	const response = await fetchImpl(BASE_PATH);
	if (!response.ok) {
		throw await toApiError(response);
	}

	const body: BoardResponse = await response.json();
	return body.tasks;
}

/** Board projection including the queue-paused flag (004 FR-021). */
export async function getIngestBoard(fetchImpl: typeof fetch = fetch): Promise<BoardResponse> {
	const response = await fetchImpl(BASE_PATH);
	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}

export async function getIngestTaskDetail(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<TaskDetail> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}`);
	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}

// 006 (contracts/task-record-api.md): a 404 is an expected, common outcome (record not
// yet written, or unparseable) — modeled as a discriminant rather than a thrown error so
// callers render the placeholder state without a try/catch.
export type TaskRecordResult = { status: 'ok'; record: TaskRecord } | { status: 'unavailable' };

export async function getIngestTaskRecord(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<TaskRecordResult> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/task-record`);
	if (response.status === 404) {
		return { status: 'unavailable' };
	}
	if (!response.ok) {
		throw await toApiError(response);
	}

	const record: TaskRecord = await response.json();
	return { status: 'ok', record };
}

/** 004: single source of truth for the submission form's prompt editor and step toggles. */
export async function getIngestSubmissionDefaults(
	fetchImpl: typeof fetch = fetch
): Promise<IngestSubmissionDefaults> {
	const response = await fetchImpl(`${BASE_PATH}/defaults`);
	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}

/** 004 FR-021: re-arms a single queued task after a Hub restart. */
export async function retriggerIngestTask(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<void> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/retrigger`, {
		method: 'POST'
	});
	if (!response.ok) {
		throw await toApiError(response);
	}
}

/** 004 FR-021: resumes automatic queue processing after a Hub restart (whole queue). */
export async function resumeIngestQueue(fetchImpl: typeof fetch = fetch): Promise<void> {
	const response = await fetchImpl(`${QUEUE_BASE_PATH}/resume`, { method: 'POST' });
	if (!response.ok) {
		throw await toApiError(response);
	}
}

/** 023 FR-010..FR-013: restarts a finally-failed task under the same id. */
export interface RestartTaskResponse {
	taskId: string;
	status: 'queued';
}

export async function restartIngestTask(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<RestartTaskResponse> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/restart`, {
		method: 'POST'
	});
	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}
