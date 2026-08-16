/**
 * 024-api-error-presentation (ADR-026): one derivation of a failed request into something a
 * person can read, replacing `httpErrorMessage.ts` and the three `REASON_MESSAGES` tables that
 * each carried a partial copy of the Hub's knowledge of its own failure modes.
 *
 * The Hub now sends the readable sentence itself, so the browser's job shrinks to two things:
 * decide what kind of failure this was, and keep the technical facts reachable without putting
 * them in front of the user.
 */

/**
 * What happened to the request — never whether its body happened to parse.
 *
 * That distinction is the one correction this feature's own spec needed. Classifying by
 * parseability makes retryability incoherent for the commonest real infrastructure failure: a
 * gateway answering 502 with an HTML page is maximally worth retrying, and a body-driven rule
 * would file it as "unexpected" and withhold the retry. What a user needs to decide is *check my
 * connection / fix my input / wait and retry*, and that follows from what happened to the request.
 */
export type ApiErrorCategory = 'unreachable' | 'declined' | 'fault' | 'unexpected';

/** Retryability is derived from the category, never stored, so FR-008 and SC-004 cannot drift. */
const RETRYABLE: ReadonlySet<ApiErrorCategory> = new Set<ApiErrorCategory>([
	'unreachable',
	'fault'
]);

export function isRetryable(category: ApiErrorCategory): boolean {
	return RETRYABLE.has(category);
}

/**
 * The generic sentence for each category, used whenever the Hub sent no prose of its own. There is
 * deliberately one for every category: no state exists in which the component has nothing readable
 * to show (FR-013).
 */
const CATEGORY_MESSAGES: Record<ApiErrorCategory, { title: string; message: string }> = {
	unreachable: {
		title: 'Cannot reach the wiki',
		message:
			'The wiki did not respond. Check your connection, then try again — nothing was submitted.'
	},
	declined: {
		title: 'Request declined',
		message: 'The wiki declined this request. Check what you submitted and try again.'
	},
	fault: {
		title: 'Something went wrong',
		message:
			'The wiki ran into a problem handling this. This is not caused by your input — try again in a moment.'
	},
	unexpected: {
		title: 'Unexpected response',
		message:
			'The wiki answered in a way this page did not expect. The technical details below may help explain why.'
	}
};

/** Beyond this the primary message stops being scannable; the full text moves to the details. */
const MAX_PRIMARY_MESSAGE_LENGTH = 220;

/** A hard cap on how much of an unrecognized body is kept for diagnosis. */
const MAX_BODY_EXCERPT_LENGTH = 500;

/** The prefix our own model-client adapter composes onto a rejected provider request. */
const PROVIDER_ERROR_PREFIX = /^(Model API error \d{3}(?: \([^)]*\))?): /;

export interface PresentedError {
	category: ApiErrorCategory;
	/** The primary readable text. Never empty, never an identifier, never serialized body content. */
	message: string;
	/** Short headline, from the envelope's `title` or the category's own. */
	title: string;
	/** True for the categories where retrying can plausibly succeed. */
	retryable: boolean;
	// Everything below belongs to the technical-detail disclosure and must never be rendered
	// as the primary message (FR-005).
	status: number | null;
	code: string | null;
	traceId: string | null;
	/** A bounded excerpt of what arrived, populated only when it was not a recognizable envelope. */
	bodyExcerpt: string | null;
	/** The untruncated text, set only when `message` had to be shortened (FR-014). */
	fullMessage: string | null;
}

/** The envelope shape the Hub sends (see specs/024-api-error-presentation/contracts). */
interface ApiErrorEnvelope {
	status?: unknown;
	title?: unknown;
	detail?: unknown;
	code?: unknown;
	traceId?: unknown;
}

/**
 * Derives a presented error from a failed `fetch` response.
 *
 * Reads the body at most once, which is why it takes the `Response` rather than a parsed body: the
 * fallback path needs the raw text it could not parse, and a `Response` body cannot be consumed
 * twice.
 */
export async function presentResponseError(response: Response): Promise<PresentedError> {
	const category: ApiErrorCategory = response.status >= 500 ? 'fault' : 'declined';
	const envelope = await readEnvelope(response);

	if (envelope === null) {
		// An unrecognizable body says nothing about what happened to the request — a faulting
		// gateway is still a fault and still worth retrying (User Story 4, scenario 5). Only a
		// declined request whose body we cannot read is genuinely "unexpected".
		const fallbackCategory: ApiErrorCategory = category === 'fault' ? 'fault' : 'unexpected';
		return present({
			category: fallbackCategory,
			status: response.status,
			bodyExcerpt: await readBodyExcerpt(response)
		});
	}

	return present({
		category,
		status: typeof envelope.status === 'number' ? envelope.status : response.status,
		title: typeof envelope.title === 'string' ? envelope.title : undefined,
		message: typeof envelope.detail === 'string' ? envelope.detail : undefined,
		code: typeof envelope.code === 'string' ? envelope.code : null,
		traceId: typeof envelope.traceId === 'string' ? envelope.traceId : null
	});
}

/**
 * Derives a presented error from a request that never produced a response — offline, DNS failure,
 * connection refused, timeout. `fetch` rejects rather than resolving in these cases.
 */
export function presentRequestFailure(): PresentedError {
	return present({ category: 'unreachable' });
}

/**
 * Derives a presented error from a response that arrived successfully but whose payload this page
 * cannot use.
 */
export function presentUnusableResponse(status: number, bodyExcerpt?: string): PresentedError {
	return present({
		category: 'unexpected',
		status,
		bodyExcerpt: bodyExcerpt ? truncate(bodyExcerpt, MAX_BODY_EXCERPT_LENGTH) : null
	});
}

/**
 * Presents a failure reason the harness already recorded for an agent run (FR-012).
 *
 * What the harness records is unchanged — this is a display-time transformation only. It strips
 * exactly one thing: the `Model API error 400 (invalid_request_error): ` prefix our own model
 * adapter composes, which is technical framing the operator wants in the details and the reader
 * does not want in the sentence. Text without that prefix is presented untouched; this deliberately
 * does not try to infer structure from arbitrary recorded strings, which is the fragility the
 * feature exists to remove.
 */
export function presentRecordedFailure(failureReason: string): PresentedError {
	const match = PROVIDER_ERROR_PREFIX.exec(failureReason);

	if (match === null) {
		return present({ category: 'fault', message: failureReason });
	}

	return present({
		category: 'fault',
		message: failureReason.slice(match[0].length),
		bodyExcerpt: match[1]
	});
}

/**
 * Turns whatever a `catch` block received into something presentable.
 *
 * Surfaces call this rather than branching on `error instanceof SomeApiError` themselves — that
 * branching, repeated per surface with a per-surface fallback string, is what this feature
 * replaces. A client error carries the presentation the response already produced; anything else
 * reaching a `catch` here means the request never completed, which is `unreachable`.
 */
export function toPresentedError(error: unknown): PresentedError {
	if (typeof error !== 'object' || error === null) {
		return presentRequestFailure();
	}

	const candidate = error as { presented?: unknown; status?: unknown; message?: unknown };

	if (isPresentedError(candidate.presented)) {
		return candidate.presented;
	}

	// An error carrying a status plainly did reach the wiki, so claiming it is unreachable would
	// be a lie — the one thing a category is for is telling the user whether to check their
	// connection. Degrade to the status-derived category and whatever message the error carries.
	if (typeof candidate.status === 'number' && candidate.status >= 400) {
		return present({
			category: candidate.status >= 500 ? 'fault' : 'declined',
			status: candidate.status,
			message: typeof candidate.message === 'string' ? candidate.message : undefined
		});
	}

	return presentRequestFailure();
}

function isPresentedError(value: unknown): value is PresentedError {
	return (
		typeof value === 'object' &&
		value !== null &&
		typeof (value as PresentedError).category === 'string' &&
		typeof (value as PresentedError).message === 'string'
	);
}

function present(input: {
	category: ApiErrorCategory;
	message?: string;
	title?: string;
	status?: number | null;
	code?: string | null;
	traceId?: string | null;
	bodyExcerpt?: string | null;
}): PresentedError {
	const defaults = CATEGORY_MESSAGES[input.category];
	const rawMessage = nonEmpty(input.message) ?? defaults.message;
	const message = truncate(rawMessage, MAX_PRIMARY_MESSAGE_LENGTH);

	return {
		category: input.category,
		title: nonEmpty(input.title) ?? defaults.title,
		message,
		retryable: isRetryable(input.category),
		status: input.status ?? null,
		code: input.code ?? null,
		traceId: input.traceId ?? null,
		bodyExcerpt: input.bodyExcerpt ?? null,
		fullMessage: message === rawMessage ? null : rawMessage
	};
}

/**
 * The envelope, or `null` when the response did not carry one. The media type is checked first
 * because that is what lets a proxy's HTML page be rejected without guessing at its contents.
 */
async function readEnvelope(response: Response): Promise<ApiErrorEnvelope | null> {
	const contentType = response.headers.get('content-type') ?? '';
	if (!contentType.includes('application/problem+json')) {
		return null;
	}

	try {
		const body = await response.clone().json();
		if (body === null || typeof body !== 'object') return null;
		// A body that carries neither of the two members we display is not usable as an envelope,
		// whatever its content type claims.
		if (typeof body.detail !== 'string' && typeof body.code !== 'string') return null;
		return body as ApiErrorEnvelope;
	} catch {
		return null;
	}
}

async function readBodyExcerpt(response: Response): Promise<string | null> {
	try {
		const text = await response.clone().text();
		return nonEmpty(text) ? truncate(text, MAX_BODY_EXCERPT_LENGTH) : null;
	} catch {
		return null;
	}
}

function nonEmpty(value: string | undefined | null): string | null {
	return typeof value === 'string' && value.trim().length > 0 ? value : null;
}

function truncate(value: string, max: number): string {
	return value.length <= max ? value : `${value.slice(0, max - 1).trimEnd()}…`;
}
