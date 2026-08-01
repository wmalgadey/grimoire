import type {
	RemediationAttachContextResponse,
	RemediationSendMessageResponse,
	RemediationTask,
	RemediationTaskDetail,
	RemediationTaskListResponse,
	RemediationTaskMessagesResponse,
	RemediationTaskState
} from '$lib/types';

const BASE_PATH = '/api/remediation-tasks';

// 015-lint-board-parity T026 (US3)/T037 (US4)/T043 (US5): contracts/remediation-task-api.md's
// full task surface — list/detail (board recovery + record-derived attached context), the
// authorize/dismiss/withdraw-authorization transitions (T037), and attach-context/
// send-message/get-history (T043). Mirrors lintApi.ts's error shape.

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

// T037 (FR-009/FR-010/FR-016): every 409 reason the authorize/dismiss/withdraw endpoints
// can return (contracts/remediation-task-api.md), turned into a human-readable message —
// mirrors lintApi.ts's REASON_MESSAGES pattern (never show the raw snake_case reason).
const REASON_MESSAGES: Record<string, string> = {
	task_not_proposed:
		'This task is no longer in the proposed state — someone else already acted on it.',
	task_not_authorized:
		'This task is no longer authorized — its authorization may already have been withdrawn.',
	execution_already_started:
		'The agent already began executing this task; it will run to a terminal outcome and can no longer be cancelled.',
	message_turn_active:
		'A message turn is already running for this task; wait for it to finish before sending another.'
};

async function parseErrorMessage(
	response: Response
): Promise<{ message: string; reason?: string }> {
	try {
		const body = await response.json();
		if (typeof body?.reason === 'string') {
			return {
				message: body.message ?? REASON_MESSAGES[body.reason] ?? body.reason,
				reason: body.reason
			};
		}
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

export interface RemediationAuthorizeResponse {
	taskId: string;
	state: RemediationTaskState;
	authorizedAt: string;
	queuePosition: number | null;
}

export interface RemediationDismissResponse {
	taskId: string;
	state: RemediationTaskState;
	dismissedAt: string;
}

export interface RemediationWithdrawResponse {
	taskId: string;
	state: RemediationTaskState;
}

/**
 * POST /api/remediation-tasks/{taskId}/authorize — `proposed → authorized` (T037,
 * FR-009). No request body. A 409 means someone else already acted on this task first
 * (contract discipline: never silence — surfaced via `RemediationApiError.reason`).
 */
export async function authorizeRemediationTask(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationAuthorizeResponse> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/authorize`, {
		method: 'POST'
	});
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	return response.json();
}

/**
 * POST /api/remediation-tasks/{taskId}/dismiss — `proposed → dismissed` (T037, FR-010).
 * No request body, no agent involvement, no wiki change.
 */
export async function dismissRemediationTask(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationDismissResponse> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/dismiss`, {
		method: 'POST'
	});
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	return response.json();
}

/**
 * POST /api/remediation-tasks/{taskId}/withdraw-authorization — `authorized → proposed`
 * (T037, FR-016). No request body. One side of the withdrawal race (spec Edge Cases):
 * a 409 with `reason: "execution_already_started"` means dispatch won the race instead —
 * the task is already executing (or past it) and can no longer be cancelled.
 */
export async function withdrawRemediationTaskAuthorization(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationWithdrawResponse> {
	const response = await fetchImpl(
		`${BASE_PATH}/${encodeURIComponent(taskId)}/withdraw-authorization`,
		{ method: 'POST' }
	);
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	return response.json();
}

/**
 * POST /api/remediation-tasks/{taskId}/context — attach additional information/
 * instructions to a task (T043, FR-011). Allowed only while `proposed`; a 409 means the
 * task moved on before this request landed.
 */
export async function attachRemediationTaskContext(
	taskId: string,
	content: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationAttachContextResponse> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/context`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ content })
	});
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	return response.json();
}

/**
 * POST /api/remediation-tasks/{taskId}/messages — send the agent a message about this
 * task (T043, FR-012). Non-blocking (202): the human message is appended to the record
 * immediately, then a bounded message turn runs; the reply arrives via
 * `remediationMessageTurnChanged` + a follow-up `fetchRemediationTaskMessages` call. A
 * 409 with `reason: "message_turn_active"` means one turn is already running for this
 * task.
 */
export async function sendRemediationTaskMessage(
	taskId: string,
	content: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationSendMessageResponse> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/messages`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ content })
	});
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	return response.json();
}

/**
 * GET /api/remediation-tasks/{taskId}/messages — the full message thread (T043, FR-014).
 * Available in every state, including terminal ones; never 409. A task with no messages
 * yet returns an empty array.
 */
export async function fetchRemediationTaskMessages(
	taskId: string,
	fetchImpl: typeof fetch = fetch
): Promise<RemediationTaskMessagesResponse> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(taskId)}/messages`);
	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new RemediationApiError(message, response.status, reason);
	}

	return response.json();
}
