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

/** 024 (ADR-026): the Hub answers every failure as application/problem+json. */
function problemResponse(status: number, code: string, detail: string): Response {
	return new Response(JSON.stringify({ status, title: 'Declined', detail, code }), {
		status,
		headers: { 'Content-Type': 'application/problem+json' }
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

test('triggerLintRun surfaces the Hub sentence and keeps the code for machines', async () => {
	const fetchImpl = async () =>
		problemResponse(
			409,
			'lint_run_active',
			'A lint run is already active. Wait for it to finish before starting another.'
		);

	const error = await triggerLintRun(fetchImpl as typeof fetch).catch((e) => e);

	expect(error).toBeInstanceOf(LintApiError);
	expect((error as LintApiError).reason).toBe('lint_run_active');
	expect((error as LintApiError).presented?.category).toBe('declined');
	expect((error as LintApiError).presented?.message).toBe(
		'A lint run is already active. Wait for it to finish before starting another.'
	);
	// The identifier stays out of what the user reads — the point of the feature.
	expect((error as LintApiError).presented?.message).not.toContain('lint_run_active');
});

test('a rejection with no readable body still reads as a sentence, never as a status line', async () => {
	// The old client displayed the Hub's raw machine identifier here, which is the defect
	// issue #85 reported. There is no such branch any more.
	const fetchImpl = async () => new Response('<html>gateway</html>', { status: 409 });

	const error = await triggerLintRun(fetchImpl as typeof fetch).catch((e) => e);

	const presented = (error as LintApiError).presented!;
	expect(presented.category).toBe('unexpected');
	expect(presented.message).not.toMatch(/^Request failed with status/);
	expect(presented.message.length).toBeGreaterThan(20);
	expect(presented.bodyExcerpt).toContain('gateway');
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
