import type {
	RemediationTask,
	RemediationTaskDetail,
	RemediationTaskListResponse
} from '$lib/types';

const BASE_PATH = '/api/remediation-tasks';

// 015-lint-board-parity T026 (US3): read surface of contracts/remediation-task-api.md —
// list (board initial-state recovery for remediation entries) and detail (record-derived
// attached context). The authorize/dismiss/withdraw and context/message calls join in
// US4/US5 (T037/T043). Mirrors lintApi.ts's error shape.

export class RemediationApiError extends Error {
	constructor(
		message: string,
		public readonly status: number,
		public readonly reason?: string
	) {
		super(message);
		this.name = 'RemediationApiError';
	}
}

async function parseErrorMessage(
	response: Response
): Promise<{ message: string; reason?: string }> {
	try {
		const body = await response.json();
		if (typeof body?.message === 'string') {
			return { message: body.message, reason: body.reason };
		}
	} catch {
		// fall through to a generic message below
	}
	return { message: `Request failed with status ${response.status}` };
}

/** GET /api/remediation-tasks — all tasks, optionally restricted to one originating lint run. */
export async function fetchRemediationTasks(
	runId?: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationTask[]> {
	const query = runId ? `?runId=${encodeURIComponent(runId)}` : '';
	const response = await fetchImpl(`${BASE_PATH}${query}`);
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	const body: RemediationTaskListResponse = await response.json();
	return body.tasks;
}

/** GET /api/remediation-tasks/{taskId} — full detail including attached context (FR-011/FR-014). */
export async function getRemediationTask(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationTaskDetail> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}`);
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	return response.json();
}
