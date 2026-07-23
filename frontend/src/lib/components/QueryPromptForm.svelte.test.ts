import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import QueryPromptForm from './QueryPromptForm.svelte';

// T029 (US1, FR-004): empty/whitespace-only and over-max-length prompts are rejected
// client-side with a clear message before submission — mirrors
// SubmissionForm.svelte.test.ts's validation-before-call pattern.

test('empty prompt is rejected client-side without calling onSubmit', async () => {
	const onSubmit = vi.fn();
	const screen = await render(QueryPromptForm, { onSubmit });

	await screen.getByTestId('query-prompt-submit-button').click();

	await expect.element(screen.getByTestId('query-prompt-error')).toHaveTextContent(
		'Enter a question'
	);
	expect(onSubmit).not.toHaveBeenCalled();
});

test('whitespace-only prompt is rejected client-side without calling onSubmit', async () => {
	const onSubmit = vi.fn();
	const screen = await render(QueryPromptForm, { onSubmit });

	await screen.getByTestId('query-prompt-input').fill('   \n\t  ');
	await screen.getByTestId('query-prompt-submit-button').click();

	await expect.element(screen.getByTestId('query-prompt-error')).toHaveTextContent(
		'Enter a question'
	);
	expect(onSubmit).not.toHaveBeenCalled();
});

test('over-max-length prompt is rejected client-side without calling onSubmit', async () => {
	const onSubmit = vi.fn();
	const screen = await render(QueryPromptForm, { onSubmit });

	// maxlength on the textarea itself would truncate typed input at the browser level,
	// so exercise the validator's own message via a value assigned past that bound.
	const overLong = 'a'.repeat(8001);
	const textarea = screen.getByTestId('query-prompt-input').element() as HTMLTextAreaElement;
	textarea.removeAttribute('maxlength');
	await screen.getByTestId('query-prompt-input').fill(overLong);
	await screen.getByTestId('query-prompt-submit-button').click();

	await expect.element(screen.getByTestId('query-prompt-error')).toHaveTextContent(
		'exceeds the maximum'
	);
	expect(onSubmit).not.toHaveBeenCalled();
});

test('valid prompt calls onSubmit with the trimmed text and clears the input', async () => {
	const onSubmit = vi.fn().mockResolvedValue(undefined);
	const screen = await render(QueryPromptForm, { onSubmit });

	await screen.getByTestId('query-prompt-input').fill('  What does ADR-004 decide?  ');
	await screen.getByTestId('query-prompt-submit-button').click();

	expect(onSubmit).toHaveBeenCalledWith('What does ADR-004 decide?');
	await expect.element(screen.getByTestId('query-prompt-input')).toHaveValue('');
});

test('disabled prop disables the input and submit button, and shows the one-turn-at-a-time hint', async () => {
	const onSubmit = vi.fn();
	const screen = await render(QueryPromptForm, { onSubmit, disabled: true });

	await expect.element(screen.getByTestId('query-prompt-input')).toBeDisabled();
	await expect.element(screen.getByTestId('query-prompt-submit-button')).toBeDisabled();
	await expect.element(screen.getByTestId('query-prompt-disabled-hint')).toBeVisible();
});
