import { presentResponseError, type PresentedError } from './apiError';
import type { LintFindingsReport, LintRun, LintRunAcceptedResponse } from '$lib/types';

const BASE_PATH = '/api/lint-runs';

export class LintApiError extends Error {
	constructor(
		message: string,
		public readonly status: number,
		public readonly reason?: string,
		/** The shared presentation of this failure; every surface renders this, not `message`. */
		public readonly presented?: PresentedError
	) {
		super(message);
		this.name = 'LintApiError';
	}
}

/**
 * 024 (ADR-026): the Hub now sends the readable sentence itself, so the snake_case→prose table
 * that used to live here is gone. Its whole purpose was to compensate for responses that carried
 * only a machine identifier — a partial copy of the Hub's knowledge of its own failure modes,
 * kept in a second language.
 */
async function toApiError(response: Response): Promise<LintApiError> {
	const presented = await presentResponseError(response);
	return new LintApiError(
		presented.message,
		response.status,
		presented.code ?? undefined,
		presented
	);
}

/** POST /api/lint-runs — a bare trigger, no request body (FR-001/FR-002). */
export async function triggerLintRun(
	fetchImpl: typeof fetch = fetch
): Promise<LintRunAcceptedResponse> {
	const response = await fetchImpl(BASE_PATH, { method: 'POST' });

	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}

/** GET /api/lint-runs/{runId} — current authoritative state (used for status polling). */
export async function getLintRun(runId: string, fetchImpl: typeof fetch = fetch): Promise<LintRun> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(runId)}`);
	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}

/** GET /api/lint-runs/latest — the most recently triggered run (recovers state across a page reload), or null if none has ever run. */
export async function getLatestLintRun(fetchImpl: typeof fetch = fetch): Promise<LintRun | null> {
	const response = await fetchImpl(`${BASE_PATH}/latest`);
	if (!response.ok) {
		throw await toApiError(response);
	}

	const body = await response.json();
	return body.runId ? body : null;
}

/** GET /api/lint-runs/{runId}/findings — the raw Findings Report markdown, once the run has one. */
export async function getLintFindings(
	runId: string,
	fetchImpl: typeof fetch = fetch
): Promise<LintFindingsReport> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(runId)}/findings`);
	if (!response.ok) {
		throw await toApiError(response);
	}

	return response.json();
}
