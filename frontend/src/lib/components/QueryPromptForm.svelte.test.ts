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

	await expect
		.element(screen.getByTestId('query-prompt-error'))
		.toHaveTextContent('Enter a question');
	expect(onSubmit).not.toHaveBeenCalled();
});

test('whitespace-only prompt is rejected client-side without calling onSubmit', async () => {
	const onSubmit = vi.fn();
	const screen = await render(QueryPromptForm, { onSubmit });

	await screen.getByTestId('query-prompt-input').fill('   \n\t  ');
	await screen.getByTestId('query-prompt-submit-button').click();

	await expect
		.element(screen.getByTestId('query-prompt-error'))
		.toHaveTextContent('Enter a question');
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

	await expect
		.element(screen.getByTestId('query-prompt-error'))
		.toHaveTextContent('exceeds the maximum');
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

// T108 (Convergence) - a bare <textarea> doesn't submit on Enter the way an <input>
// does, so a real user expected the standard Ctrl+Enter/Cmd+Enter multi-line-submit
// convention; plain Enter must keep inserting a newline rather than submitting.
test('Ctrl+Enter in the textarea submits the form', async () => {
	const onSubmit = vi.fn().mockResolvedValue(undefined);
	const screen = await render(QueryPromptForm, { onSubmit });

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	const textarea = screen.getByTestId('query-prompt-input').element() as HTMLTextAreaElement;
	textarea.dispatchEvent(
		new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true, cancelable: true })
	);

	await expect.poll(() => onSubmit).toHaveBeenCalledWith('What does ADR-004 decide?');
});

test('Cmd+Enter in the textarea submits the form', async () => {
	const onSubmit = vi.fn().mockResolvedValue(undefined);
	const screen = await render(QueryPromptForm, { onSubmit });

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	const textarea = screen.getByTestId('query-prompt-input').element() as HTMLTextAreaElement;
	textarea.dispatchEvent(
		new KeyboardEvent('keydown', { key: 'Enter', metaKey: true, bubbles: true, cancelable: true })
	);

	await expect.poll(() => onSubmit).toHaveBeenCalledWith('What does ADR-004 decide?');
});

test('plain Enter in the textarea does not submit the form', async () => {
	const onSubmit = vi.fn().mockResolvedValue(undefined);
	const screen = await render(QueryPromptForm, { onSubmit });

	await screen.getByTestId('query-prompt-input').fill('What does ADR-004 decide?');
	const textarea = screen.getByTestId('query-prompt-input').element() as HTMLTextAreaElement;
	textarea.dispatchEvent(
		new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
	);

	expect(onSubmit).not.toHaveBeenCalled();
});

test('disabled prop disables the input and submit button', async () => {
	const onSubmit = vi.fn();
	const screen = await render(QueryPromptForm, { onSubmit, disabled: true });

	await expect.element(screen.getByTestId('query-prompt-input')).toBeDisabled();
	await expect.element(screen.getByTestId('query-prompt-submit-button')).toBeDisabled();
});

// FR-008's rule is stated whether or not a turn is running, so nobody has to trigger the
// disabled state to learn it.
test('the one-question-at-a-time rule is always on the composer', async () => {
	const screen = await render(QueryPromptForm, { onSubmit: vi.fn() });

	await expect
		.element(screen.getByTestId('query-context-hint'))
		.toHaveTextContent('One question at a time');
});

// The design's composer: while an answer streams the Ask button becomes Stop and the input
// says what it is waiting for (chat 3).
test('while answering, Ask becomes Stop and the input says what it is waiting for', async () => {
	const onStop = vi.fn();
	const screen = await render(QueryPromptForm, { onSubmit: vi.fn(), disabled: true, onStop });

	await expect.element(screen.getByTestId('query-prompt-submit-button')).not.toBeInTheDocument();
	await expect
		.element(screen.getByTestId('query-prompt-input'))
		.toHaveAttribute('placeholder', 'waiting for the current answer…');

	await screen.getByTestId('query-prompt-stop-button').click();

	expect(onStop).toHaveBeenCalledOnce();
});

test('the model row appears only when the thread offers a choice, and reports the pick', async () => {
	const onModelChange = vi.fn();
	const without = await render(QueryPromptForm, { onSubmit: vi.fn() });
	await expect.element(without.getByTestId('ask-model-picker')).not.toBeInTheDocument();
	without.unmount();

	const withPicker = await render(QueryPromptForm, {
		onSubmit: vi.fn(),
		model: 'Claude Sonnet 4.5',
		onModelChange
	});
	await withPicker.getByTestId('model-option').filter({ hasText: 'Claude Opus 4.1' }).click();

	expect(onModelChange).toHaveBeenCalledWith('Claude Opus 4.1');
});
