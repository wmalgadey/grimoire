import type { QueryPriorTurn, QueryTurn, QueryTurnAcceptedResponse } from '$lib/types';

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

async function parseErrorMessage(response: Response): Promise<{ message: string; reason?: string }> {
	try {
		const body = await response.json();
		if (typeof body?.message === 'string') return { message: body.message };
		if (typeof body?.reason === 'string') return { message: body.reason, reason: body.reason };
	} catch {
		// fall through to a generic message below
	}
	return { message: `Request failed with status ${response.status}` };
}

/**
 * Submits one Query Turn (contracts/query-conversation-api.md). `priorTurns` is empty/
 * absent for a conversation's first turn; follow-ups send the full history so far,
 * including partial answers of interrupted turns (FR-009).
 */
export async function submitQueryTurn(
	conversationId: string,
	prompt: string,
	priorTurns: QueryPriorTurn[] = [],
	fetchImpl: typeof fetch = fetch
): Promise<QueryTurnAcceptedResponse> {
	const response = await fetchImpl(`${CONVERSATIONS_BASE_PATH}/${encodeURIComponent(conversationId)}/turns`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ prompt, priorTurns })
	});

	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new QuerySubmissionApiError(message, response.status, reason);
	}

	return response.json();
}

/** GET /api/query-turns/{turnId} — current authoritative state (used on reconnect). */
export async function getQueryTurn(turnId: string, fetchImpl: typeof fetch = fetch): Promise<QueryTurn> {
	const response = await fetchImpl(`${TURNS_BASE_PATH}/${encodeURIComponent(turnId)}`);
	if (!response.ok) {
		const { message } = await parseErrorMessage(response);
		throw new QuerySubmissionApiError(message, response.status);
	}

	return response.json();
}

/** POST /api/query-turns/{turnId}/interrupt (FR-006). */
export async function interruptQueryTurn(turnId: string, fetchImpl: typeof fetch = fetch): Promise<QueryTurn> {
	const response = await fetchImpl(`${TURNS_BASE_PATH}/${encodeURIComponent(turnId)}/interrupt`, {
		method: 'POST'
	});
	if (!response.ok) {
		const { message } = await parseErrorMessage(response);
		throw new QuerySubmissionApiError(message, response.status);
	}

	return response.json();
}
