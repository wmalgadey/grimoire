import { presentResponseError, type PresentedError } from './apiError';
import type { QueryTurn, QueryTurnAcceptedResponse } from '$lib/types';

const CONVERSATIONS_BASE_PATH = '/api/query-conversations';
const TURNS_BASE_PATH = '/api/query-turns';

export class QuerySubmissionApiError extends Error {
	constructor(
		message: string,
		public readonly status: number,
		public readonly reason?: string,
		/** The shared presentation of this failure; every surface renders this, not `message`. */
		public readonly presented?: PresentedError
	) {
		super(message);
		this.name = 'QuerySubmissionApiError';
	}
}

/**
 * 024 (ADR-026): the Hub sends the readable sentence; the snake_case→prose table that used to sit
 * here is gone, along with this client's private disagreement with the other two about whether
 * `message` or `reason` won.
 */
async function toApiError(response: Response): Promise<QuerySubmissionApiError> {
	const presented = await presentResponseError(response);
	return new QuerySubmissionApiError(
		presented.message,
		response.status,
		presented.code ?? undefined,
		presented
	);
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
		throw await toApiError(response);
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
		throw await toApiError(response);
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
		throw await toApiError(response);
	}

	return response.json();
}
