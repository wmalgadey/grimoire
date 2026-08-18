import { expect, test } from 'vitest';
import { getServerVersion } from './serverVersionApi';

function jsonResponse(status: number, body: unknown): Response {
	return new Response(JSON.stringify(body), {
		status,
		headers: { 'Content-Type': 'application/json' }
	});
}

test('reads the version the Hub reports from GET /api/version', async () => {
	let requestedPath: string | undefined;
	const fetchImpl = async (input: RequestInfo | URL) => {
		requestedPath = String(input);
		return jsonResponse(200, { version: '0.0.26' });
	};

	const version = await getServerVersion(fetchImpl as typeof fetch);

	expect(requestedPath).toBe('/api/version');
	expect(version).toBe('0.0.26');
});

// The three ways the answer can be unusable. All of them mean the same thing to the caller —
// the version is not established — and none of them may surface as a failed promise: this
// backs a line in a hover panel, and a rejection here would reach a caller that has nothing
// useful to do with it (see the note in serverVersionApi.ts).
test('reports an unknown version rather than throwing when the Hub answers with an error', async () => {
	const fetchImpl = async () => jsonResponse(503, { title: 'Unavailable' });

	await expect(getServerVersion(fetchImpl as typeof fetch)).resolves.toBeNull();
});

test('reports an unknown version rather than throwing when the request never lands', async () => {
	const fetchImpl = async () => {
		throw new TypeError('Failed to fetch');
	};

	await expect(getServerVersion(fetchImpl as typeof fetch)).resolves.toBeNull();
});

test('reports an unknown version when the response carries no usable version field', async () => {
	const fetchImpl = async () => jsonResponse(200, { version: '' });

	await expect(getServerVersion(fetchImpl as typeof fetch)).resolves.toBeNull();
});
