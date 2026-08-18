const BASE_PATH = '/api/version';

/**
 * GET /api/version — the version of the Hub this browser is talking to (ADR-027 computes it;
 * `HubVersionEndpoints` serves it). Read by the connection indicator so "connected" can say
 * connected to *what*: after a redeploy a long-lived tab may still be running the previous
 * bundle against a new server, and the version in the hover panel is what makes that visible.
 *
 * Deliberately does not throw and carries no `*ApiError` class of its own. Every other service
 * here reports a failure to the user because the user asked for something; nobody asks for this
 * — it backs a decorative line in a hover panel. A Hub too broken to answer it is already
 * saying so through the connection state beside it, and an error path would only add a second,
 * noisier way to say the same thing.
 *
 * @returns the version string, or `null` when it could not be established.
 */
export async function getServerVersion(fetchImpl: typeof fetch = fetch): Promise<string | null> {
	try {
		const response = await fetchImpl(BASE_PATH);
		if (!response.ok) {
			return null;
		}

		const body = await response.json();
		return typeof body?.version === 'string' && body.version.length > 0 ? body.version : null;
	} catch {
		return null;
	}
}
