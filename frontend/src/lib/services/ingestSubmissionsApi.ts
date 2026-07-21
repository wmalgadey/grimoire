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

const BASE_PATH = '/api/ingest-submissions';
const QUEUE_BASE_PATH = '/api/ingest-queue';

export class IngestSubmissionApiError extends Error {
	constructor(
		message: string,
		public readonly status: number
	) {
		super(message);
		this.name = 'IngestSubmissionApiError';
	}
}

async function parseErrorMessage(response: Response): Promise<string> {
	try {
		const body = await response.json();
		if (typeof body?.message === 'string') return body.message;
	} catch {
		// fall through to a generic message below
	}
	return `Request failed with status ${response.status}`;
}

// 004: optional per-submission steering prompt and convert-step overrides (FR-006, FR-011).
// Both stay optional so a caller that doesn't touch either reproduces feature 003 exactly.
export interface SubmissionOptions {
	userPrompt?: string;
	convertSteps?: ConvertStepConfig;
	fetchImpl?: typeof fetch;
}

export async function submitUrl(
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
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	return response.json();
}

export async function submitFile(
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
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	return response.json();
}

export async function listBoard(fetchImpl: typeof fetch = fetch): Promise<BoardTask[]> {
	const response = await fetchImpl(BASE_PATH);
	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	const body: BoardResponse = await response.json();
	return body.tasks;
}

/** Board projection including the queue-paused flag (004 FR-021). */
export async function getBoard(fetchImpl: typeof fetch = fetch): Promise<BoardResponse> {
	const response = await fetchImpl(BASE_PATH);
	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	return response.json();
}

export async function getTaskDetail(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<TaskDetail> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}`);
	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	return response.json();
}

// 006 (contracts/task-record-api.md): a 404 is an expected, common outcome (record not
// yet written, or unparseable) — modeled as a discriminant rather than a thrown error so
// callers render the placeholder state without a try/catch.
export type TaskRecordResult = { status: 'ok'; record: TaskRecord } | { status: 'unavailable' };

export async function getTaskRecord(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<TaskRecordResult> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/task-record`);
	if (response.status === 404) {
		return { status: 'unavailable' };
	}
	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	const record: TaskRecord = await response.json();
	return { status: 'ok', record };
}

/** 004: single source of truth for the submission form's prompt editor and step toggles. */
export async function getSubmissionDefaults(
	fetchImpl: typeof fetch = fetch
): Promise<IngestSubmissionDefaults> {
	const response = await fetchImpl(`${BASE_PATH}/defaults`);
	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	return response.json();
}

/** 004 FR-021: re-arms a single queued task after a Hub restart. */
export async function retriggerTask(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<void> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/retrigger`, {
		method: 'POST'
	});
	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}
}

/** 004 FR-021: resumes automatic queue processing after a Hub restart (whole queue). */
export async function resumeQueue(fetchImpl: typeof fetch = fetch): Promise<void> {
	const response = await fetchImpl(`${QUEUE_BASE_PATH}/resume`, { method: 'POST' });
	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}
}
