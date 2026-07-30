import { expect, test } from 'vitest';
import {
	LintApiError,
	getLatestLintRun,
	getLintFindings,
	getLintRun,
	triggerLintRun
} from './lintApi';

function jsonResponse(status: number, body: unknown): Response {
	return new Response(JSON.stringify(body), {
		status,
		headers: { 'Content-Type': 'application/json' }
	});
}

test('triggerLintRun POSTs with no request body and returns the accepted response', async () => {
	let capturedMethod: string | undefined;
	let capturedBody: BodyInit | null | undefined;
	const fetchImpl = async (_input: RequestInfo | URL, init?: RequestInit) => {
		capturedMethod = init?.method;
		capturedBody = init?.body;
		return jsonResponse(202, {
			runId: '2026-07-30-lint-abc',
			status: 'running',
			triggeredAt: new Date().toISOString()
		});
	};

	const accepted = await triggerLintRun(fetchImpl as typeof fetch);

	expect(capturedMethod).toBe('POST');
	expect(capturedBody).toBeUndefined();
	expect(accepted.runId).toBe('2026-07-30-lint-abc');
	expect(accepted.status).toBe('running');
});

test('triggerLintRun maps a 409 busy rejection to a human-readable message', async () => {
	const fetchImpl = async () =>
		jsonResponse(409, {
			reason: 'lint_run_active',
			message: 'A Lint Run is already active. Wait for it to finish before triggering another.'
		});

	const error = await triggerLintRun(fetchImpl as typeof fetch).catch((e) => e);

	expect(error).toBeInstanceOf(LintApiError);
	expect((error as LintApiError).reason).toBe('lint_run_active');
	expect((error as LintApiError).message).toBe(
		'A Lint Run is already active. Wait for it to finish before triggering another.'
	);
});

test('triggerLintRun falls back to the REASON_MESSAGES map when the Hub sends no message', async () => {
	const fetchImpl = async () => jsonResponse(409, { reason: 'lint_run_active' });

	const error = await triggerLintRun(fetchImpl as typeof fetch).catch((e) => e);

	expect((error as LintApiError).message).toBe(
		'A lint run is already in progress — wait for it to finish before triggering another.'
	);
});

test('getLintRun returns the parsed run status', async () => {
	const fetchImpl = async () =>
		jsonResponse(200, {
			runId: 'r-1',
			status: 'completed',
			triggeredAt: '2026-07-30T10:00:00Z',
			completedAt: '2026-07-30T10:04:00Z',
			failureReason: null,
			hasFindingsReport: true
		});

	const run = await getLintRun('r-1', fetchImpl as typeof fetch);

	expect(run.status).toBe('completed');
	expect(run.hasFindingsReport).toBe(true);
});

test('getLatestLintRun returns null when no run has ever been triggered', async () => {
	const fetchImpl = async () => jsonResponse(200, { runId: null });

	const run = await getLatestLintRun(fetchImpl as typeof fetch);

	expect(run).toBeNull();
});

test('getLintFindings returns the raw report content', async () => {
	const fetchImpl = async () =>
		jsonResponse(200, {
			runId: 'r-1',
			content: '## Content Quality\n\nNo content-quality findings.\n'
		});

	const report = await getLintFindings('r-1', fetchImpl as typeof fetch);

	expect(report.content).toContain('No content-quality findings.');
});

test('getLintFindings surfaces a 404 as a LintApiError', async () => {
	const fetchImpl = async () =>
		jsonResponse(404, { message: "Findings Report for run 'r-1' is not available." });

	const error = await getLintFindings('r-1', fetchImpl as typeof fetch).catch((e) => e);

	expect(error).toBeInstanceOf(LintApiError);
	expect((error as LintApiError).status).toBe(404);
});
