import type { QueryTurn, QueryTurnAcceptedResponse } from '$lib/types';

const CONVERSATIONS_BASE_PATH = '/api/query-conversations';
const TURNS_BASE_PATH = '/api/query-turns';

export class QuerySubmissionApiError extends Error {
	constructor(
		message: string,
		public readonly status: number,
		public readonly reason?: string
	) {
		super(message);
		this.name = 'QuerySubmissionApiError';
	}
}

// spec.md Edge Cases / Assumptions: rejections beyond the concurrency limit or an
// already-active conversation turn must show a clear, human-readable "busy" message —
// not the raw snake_case machine reason code the Hub returns.
const REASON_MESSAGES: Record<string, string> = {
	query_concurrency_limit_reached: 'The wiki is busy right now — please try again in a moment.',
	conversation_already_active: 'This conversation already has a question in progress.'
};

async function parseErrorMessage(
	response: Response
): Promise<{ message: string; reason?: string }> {
	try {
		const body = await response.json();
		if (typeof body?.message === 'string') return { message: body.message };
		if (typeof body?.reason === 'string') {
			return { message: REASON_MESSAGES[body.reason] ?? body.reason, reason: body.reason };
		}
	} catch {
		// fall through to a generic message below
	}
	return { message: `Request failed with status ${response.status}` };
}

/**
 * Submits one Query Turn (contracts/query-conversation-api.md, revised by ADR-014):
 * the body carries only the prompt — the Hub builds the follow-up context from its
 * Conversation Record, so the browser no longer sends `priorTurns`.
 */
export async function submitQueryTurn(
	conversationId: string,
	prompt: string,
	fetchImpl: typeof fetch = fetch
): Promise<QueryTurnAcceptedResponse> {
	const response = await fetchImpl(
		`${CONVERSATIONS_BASE_PATH}/${encodeURIComponent(conversationId)}/turns`,
		{
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ prompt })
		}
	);

	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new QuerySubmissionApiError(message, response.status, reason);
	}

	return response.json();
}

/** GET /api/query-turns/{turnId} — current authoritative state (used on reconnect). */
export async function getQueryTurn(
	turnId: string,
	fetchImpl: typeof fetch = fetch
): Promise<QueryTurn> {
	const response = await fetchImpl(`${TURNS_BASE_PATH}/${encodeURIComponent(turnId)}`);
	if (!response.ok) {
		const { message } = await parseErrorMessage(response);
		throw new QuerySubmissionApiError(message, response.status);
	}

	return response.json();
}

/** POST /api/query-turns/{turnId}/interrupt (FR-006). */
export async function interruptQueryTurn(
	turnId: string,
	fetchImpl: typeof fetch = fetch
): Promise<QueryTurn> {
	const response = await fetchImpl(`${TURNS_BASE_PATH}/${encodeURIComponent(turnId)}/interrupt`, {
		method: 'POST'
	});
	if (!response.ok) {
		const { message } = await parseErrorMessage(response);
		throw new QuerySubmissionApiError(message, response.status);
	}

	return response.json();
}
