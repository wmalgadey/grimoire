import type {
	BoardTask,
	IngestSubmissionKind,
	SubmissionAcceptedResponse,
	TaskDetail
} from '$lib/types';

const BASE_PATH = '/api/ingest-submissions';

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

export async function submitUrl(
	url: string,
	fetchImpl: typeof fetch = fetch
): Promise<SubmissionAcceptedResponse> {
	const response = await fetchImpl(BASE_PATH, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ kind: 'url', url })
	});

	if (!response.ok) {
		throw new IngestSubmissionApiError(await parseErrorMessage(response), response.status);
	}

	return response.json();
}

export async function submitFile(
	kind: Exclude<IngestSubmissionKind, 'url'>,
	file: File,
	fetchImpl: typeof fetch = fetch
): Promise<SubmissionAcceptedResponse> {
	const formData = new FormData();
	formData.set('kind', kind);
	formData.set('file', file);

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

	const body: { tasks: BoardTask[] } = await response.json();
	return body.tasks;
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
