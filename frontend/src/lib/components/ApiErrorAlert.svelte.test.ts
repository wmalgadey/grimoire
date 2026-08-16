import { expect, test, vi } from 'vitest';
import { render } from 'vitest-browser-svelte';
import ApiErrorAlert from './ApiErrorAlert.svelte';
import type { ApiErrorCategory, PresentedError } from '$lib/services/apiError';

/**
 * 024-api-error-presentation (T032, T038, T044): the shared presentation's own contract — what
 * reaches the primary area, what stays behind the disclosure, and which categories offer a retry.
 */

function presented(overrides: Partial<PresentedError> = {}): PresentedError {
	return {
		category: 'declined',
		title: 'Conversation is busy',
		message: 'This conversation is still working on the previous question.',
		retryable: false,
		status: 409,
		code: 'conversation_already_active',
		traceId: 'trace-abc-123',
		bodyExcerpt: null,
		fullMessage: null,
		...overrides
	};
}

// ---------------------------------------------------------------------------
// SC-002 / SC-003 — the primary/technical split (T032)
// ---------------------------------------------------------------------------

test('the primary area shows the sentence and none of the technical facts', async () => {
	const screen = await render(ApiErrorAlert, { error: presented() });

	await expect
		.element(screen.getByTestId('api-error-message'))
		.toHaveTextContent('This conversation is still working on the previous question.');

	// The identifier, the status and the correlation id are diagnostics. Putting any of them in
	// front of the user is the defect issue #85 reported.
	expect(screen.container.querySelector('[data-testid="api-error-details"]')).toBeNull();
	const primary = screen.container.querySelector('[data-testid="api-error-message"]')!;
	expect(primary.textContent).not.toContain('conversation_already_active');
	expect(primary.textContent).not.toContain('409');
	expect(primary.textContent).not.toContain('trace-abc-123');
});

test('opening the disclosure reveals the status, the code and the correlation id', async () => {
	const screen = await render(ApiErrorAlert, { error: presented() });

	await screen.getByTestId('api-error-details-toggle').click();

	await expect.element(screen.getByTestId('api-error-detail-status')).toHaveTextContent('409');
	await expect
		.element(screen.getByTestId('api-error-detail-code'))
		.toHaveTextContent('conversation_already_active');
	await expect
		.element(screen.getByTestId('api-error-detail-trace-id'))
		.toHaveTextContent('trace-abc-123');
});

test('a correlation id the response never carried is omitted, not rendered blank', async () => {
	const screen = await render(ApiErrorAlert, { error: presented({ traceId: null }) });

	await screen.getByTestId('api-error-details-toggle').click();

	expect(screen.container.querySelector('[data-testid="api-error-detail-trace-id"]')).toBeNull();
});

test('an elided message keeps its full text in the disclosure', async () => {
	const full = `The provider rejected this request. ${'x'.repeat(400)}`;
	const screen = await render(ApiErrorAlert, {
		error: presented({ message: 'The provider rejected this request. xxx…', fullMessage: full })
	});

	await screen.getByTestId('api-error-details-toggle').click();

	await expect.element(screen.getByTestId('api-error-detail-full-message')).toHaveTextContent(full);
});

test('an unrecognized response body is shown only inside the disclosure', async () => {
	const screen = await render(ApiErrorAlert, {
		error: presented({ category: 'unexpected', bodyExcerpt: '<html>Bad Gateway</html>' })
	});

	const primary = screen.container.querySelector('[data-testid="api-error-message"]')!;
	expect(primary.textContent).not.toContain('Bad Gateway');

	await screen.getByTestId('api-error-details-toggle').click();
	await expect
		.element(screen.getByTestId('api-error-detail-body'))
		.toHaveTextContent('Bad Gateway');
});

test('a new error closes the disclosure the previous one had open', async () => {
	// The component instance survives when one failure replaces another on the same surface. If
	// the disclosure stayed open, the next error would expose its internals with no user action.
	const screen = await render(ApiErrorAlert, { error: presented() });
	await screen.getByTestId('api-error-details-toggle').click();
	await expect.element(screen.getByTestId('api-error-details')).toBeInTheDocument();

	await screen.rerender({ error: presented({ code: 'a_different_failure' }) });

	expect(screen.container.querySelector('[data-testid="api-error-details"]')).toBeNull();
});

// ---------------------------------------------------------------------------
// SC-004 — categories and retry (T044)
// ---------------------------------------------------------------------------

const CATEGORIES: ApiErrorCategory[] = ['unreachable', 'declined', 'fault', 'unexpected'];

test('every category is rendered distinguishably', async () => {
	const seen = new Set<string>();

	for (const category of CATEGORIES) {
		const screen = await render(ApiErrorAlert, { error: presented({ category }) });
		const region = screen.container.querySelector('[data-testid="api-error"]')!;
		seen.add(region.getAttribute('data-category')!);
	}

	expect(seen).toEqual(new Set(CATEGORIES));
});

test('retry is offered for exactly the retryable categories', async () => {
	for (const category of CATEGORIES) {
		const retryable = category === 'unreachable' || category === 'fault';
		const screen = await render(ApiErrorAlert, {
			error: presented({ category, retryable }),
			onRetry: () => {}
		});

		const retry = screen.container.querySelector('[data-testid="api-error-retry"]');
		expect(retry === null).toBe(!retryable);
	}
});

test('retry runs the action the surface supplied', async () => {
	const onRetry = vi.fn();
	const screen = await render(ApiErrorAlert, {
		error: presented({ category: 'fault', retryable: true }),
		onRetry
	});

	await screen.getByTestId('api-error-retry').click();

	expect(onRetry).toHaveBeenCalledTimes(1);
});

test('a surface that supplies no retry action gets no retry control', async () => {
	const screen = await render(ApiErrorAlert, {
		error: presented({ category: 'fault', retryable: true })
	});

	expect(screen.container.querySelector('[data-testid="api-error-retry"]')).toBeNull();
});

// ---------------------------------------------------------------------------
// FR-009 / FR-011 — announcement and dismissal (T038)
// ---------------------------------------------------------------------------

test('the error is announced to assistive technology', async () => {
	const screen = await render(ApiErrorAlert, { error: presented() });

	const region = screen.container.querySelector('[data-testid="api-error"]')!;
	expect(region.getAttribute('role')).toBe('alert');
});

test('rendering the error does not steal keyboard focus', async () => {
	const before = document.activeElement;

	await render(ApiErrorAlert, { error: presented() });

	// A user mid-sentence in a form must hear what happened without losing their place.
	expect(document.activeElement).toBe(before);
});

test('dismissing raises the surface handler that clears it', async () => {
	const onDismiss = vi.fn();
	const screen = await render(ApiErrorAlert, { error: presented(), onDismiss });

	await screen.getByTestId('api-error-dismiss').click();

	expect(onDismiss).toHaveBeenCalledTimes(1);
});

test('a surface that cannot dismiss gets no dismiss control', async () => {
	const screen = await render(ApiErrorAlert, { error: presented() });

	expect(screen.container.querySelector('[data-testid="api-error-dismiss"]')).toBeNull();
});

test('the test id is namespaced per surface so several alerts can coexist', async () => {
	const screen = await render(ApiErrorAlert, {
		error: presented(),
		testId: 'lint-trigger-error'
	});

	await expect.element(screen.getByTestId('lint-trigger-error-message')).toBeVisible();
});
