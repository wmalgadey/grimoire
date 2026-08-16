/**
 * 023 T052: the Hub answers a rejected request with a JSON body carrying a human-readable
 * reason — `{ message }` for most endpoints, `{ reason }` for restart's 409
 * (contracts/http-api.md). Discarding it and showing the bare status code is how an
 * actionable "no lint run may start while remediation tasks are unresolved" becomes an
 * unhelpful "failed with status 409".
 *
 * This is the shape `ingestSubmissionsApi.parseErrorMessage` already proved; it lives here
 * so every client reads the body the same way instead of each re-deciding.
 */
export async function parseHttpErrorMessage(response: Response): Promise<string> {
	try {
		const body = await response.json();
		if (typeof body?.message === 'string') return body.message;
		if (typeof body?.reason === 'string') return body.reason;
	} catch {
		// Empty, HTML, or otherwise unparseable body — fall through to the status text.
	}
	return `Request failed with status ${response.status}`;
}
