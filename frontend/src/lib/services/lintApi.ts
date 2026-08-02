import type { LintFindingsReport, LintRun, LintRunAcceptedResponse } from '$lib/types';

const BASE_PATH = '/api/lint-runs';

export class LintApiError extends Error {
	constructor(
		message: string,
		public readonly status: number,
		public readonly reason?: string
	) {
		super(message);
		this.name = 'LintApiError';
	}
}

// spec.md Edge Cases: a trigger while a run is active must show a clear, human-readable
// "busy" message — not the raw snake_case machine reason code the Hub returns (mirrors
// querySubmissionApi.ts's REASON_MESSAGES pattern).
const REASON_MESSAGES: Record<string, string> = {
	lint_run_active:
		'A lint run is already in progress — wait for it to finish before triggering another.',
	// 015-lint-board-parity T018 (FR-004/SC-004, contracts/lint-board-api.md): the second
	// distinguishable blocked-trigger reason.
	unresolved_remediation_tasks:
		'Remediation tasks from the previous lint run are still unresolved — authorize, dismiss, or wait for them to finish before starting a new run.'
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
		if (typeof body?.message === 'string') return { message: body.message };
	} catch {
		// fall through to a generic message below
	}
	return { message: `Request failed with status ${response.status}` };
}

/** POST /api/lint-runs — a bare trigger, no request body (FR-001/FR-002). */
export async function triggerLintRun(
	fetchImpl: typeof fetch = fetch
): Promise<LintRunAcceptedResponse> {
	const response = await fetchImpl(BASE_PATH, { method: 'POST' });

	if (!response.ok) {
		const { message, reason } = await parseErrorMessage(response);
		throw new LintApiError(message, response.status, reason);
	}

	return response.json();
}

/** GET /api/lint-runs/{runId} — current authoritative state (used for status polling). */
export async function getLintRun(runId: string, fetchImpl: typeof fetch = fetch): Promise<LintRun> {
	const response = await fetchImpl(`${BASE_PATH}/${encodeURIComponent(runId)}`);
	if (!response.ok) {
		const { message } = await parseErrorMessage(response);
		throw new LintApiError(message, response.status);
	}

	return response.json();
}

/** GET /api/lint-runs/latest — the most recently triggered run (recovers state across a page reload), or null if none has ever run. */
export async function getLatestLintRun(fetchImpl: typeof fetch = fetch): Promise<LintRun | null> {
	const response = await fetchImpl(`${BASE_PATH}/latest`);
	if (!response.ok) {
		const { message } = await parseErrorMessage(response);
		throw new LintApiError(message, response.status);
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
		const { message } = await parseErrorMessage(response);
		throw new LintApiError(message, response.status);
	}

	return response.json();
}
