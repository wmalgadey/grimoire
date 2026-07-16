import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import SubmissionForm from './SubmissionForm.svelte';

// T023 (US1): submitting via SubmissionForm shows an immediate acceptance message and a
// non-terminal task state, for both file and URL submissions (Acceptance Scenarios 1-2).
// T024 (US2/US3): the prompt editor is prefilled/editable and step toggles render per kind.

const DEFAULT_PROMPT = 'Please integrate the following source into the wiki.';

vi.mock('$lib/services/ingestSubmissionsApi', async () => {
	const actual = await vi.importActual<typeof import('$lib/services/ingestSubmissionsApi')>(
		'$lib/services/ingestSubmissionsApi'
	);
	return {
		...actual,
		submitUrl: vi.fn(),
		submitFile: vi.fn(),
		getSubmissionDefaults: vi.fn()
	};
});

async function mockDefaults() {
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.getSubmissionDefaults).mockResolvedValue({
		defaultUserPrompt: DEFAULT_PROMPT,
		userPromptMaxLength: 8000,
		convertSteps: [
			{
				name: 'markitdown',
				appliesTo: ['url', 'pdf_file', 'office_file'],
				requiredFor: ['pdf_file', 'office_file'],
				defaultEnabled: true
			}
		]
	});
}

test('URL submission shows immediate acceptance with a non-terminal status', async () => {
	await mockDefaults();
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitUrl).mockResolvedValue({
		taskId: 'task-url-1',
		status: 'received',
		sourceKind: 'url',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(SubmissionForm);

	await expect
		.element(screen.getByTestId('submission-user-prompt-input'))
		.toHaveValue(DEFAULT_PROMPT);

	await screen.getByTestId('submission-url-input').fill('https://example.test/article');
	await screen.getByTestId('submission-submit-button').click();

	await expect.element(screen.getByTestId('submission-accepted')).toBeVisible();
	await expect.element(screen.getByTestId('submission-accepted')).toHaveTextContent('task-url-1');
	await expect.element(screen.getByTestId('status-badge')).toHaveTextContent('Received');
	// Untouched prompt/steps: the call signature matches feature 003 exactly (FR-015).
	expect(api.submitUrl).toHaveBeenCalledWith('https://example.test/article');
});

test('File submission shows immediate acceptance with a non-terminal status', async () => {
	await mockDefaults();
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitFile).mockResolvedValue({
		taskId: 'task-file-1',
		status: 'received',
		sourceKind: 'markdown_file',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(SubmissionForm);
	await expect
		.element(screen.getByTestId('submission-user-prompt-input'))
		.toHaveValue(DEFAULT_PROMPT);

	await screen.getByLabelText('File').click();
	const file = new File(['# Hello'], 'note.md', { type: 'text/markdown' });
	// Real browser (no jsdom): set files directly on the input then dispatch a change event.
	const input = screen.getByTestId('submission-file-input').element() as HTMLInputElement;
	const dataTransfer = new DataTransfer();
	dataTransfer.items.add(file);
	input.files = dataTransfer.files;
	input.dispatchEvent(new Event('change', { bubbles: true }));

	await screen.getByTestId('submission-submit-button').click();

	await expect.element(screen.getByTestId('submission-accepted')).toBeVisible();
	await expect.element(screen.getByTestId('submission-accepted')).toHaveTextContent('task-file-1');
	expect(api.submitFile).toHaveBeenCalledWith('markdown_file', file);
});

test('Missing URL shows a validation error and does not call the API', async () => {
	await mockDefaults();
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitUrl).mockClear();

	const screen = await render(SubmissionForm);
	await screen.getByTestId('submission-submit-button').click();

	await expect.element(screen.getByTestId('submission-error')).toBeVisible();
	expect(api.submitUrl).not.toHaveBeenCalled();
});

test('Editing the user prompt sends it as a custom override', async () => {
	await mockDefaults();
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitUrl).mockResolvedValue({
		taskId: 'task-url-2',
		status: 'received',
		sourceKind: 'url',
		acceptedAt: new Date().toISOString(),
		userPromptSource: 'custom'
	});

	const screen = await render(SubmissionForm);
	await expect
		.element(screen.getByTestId('submission-user-prompt-input'))
		.toHaveValue(DEFAULT_PROMPT);

	await screen.getByTestId('submission-url-input').fill('https://example.test/steered');
	await screen
		.getByTestId('submission-user-prompt-input')
		.fill('Focus on the security claims only.');
	await screen.getByTestId('submission-submit-button').click();

	await expect.element(screen.getByTestId('submission-accepted')).toBeVisible();
	expect(api.submitUrl).toHaveBeenCalledWith('https://example.test/steered', {
		userPrompt: 'Focus on the security claims only.',
		convertSteps: undefined
	});
});

test('Convert step toggle is required (disabled) for PDF submissions', async () => {
	await mockDefaults();
	const screen = await render(SubmissionForm);

	await screen.getByLabelText('File').click();
	await screen.getByTestId('submission-kind-select').selectOptions('pdf_file');

	const toggle = screen.getByTestId('submission-convert-step-markitdown');
	await expect.element(toggle).toBeVisible();
	const toggleElement = toggle.element() as HTMLInputElement;
	expect(toggleElement.disabled).toBe(true);
	expect(toggleElement.checked).toBe(true);
});

test('Convert step toggle is optional and can be disabled for URL submissions', async () => {
	await mockDefaults();
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitUrl).mockResolvedValue({
		taskId: 'task-url-3',
		status: 'received',
		sourceKind: 'url',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(SubmissionForm);
	await screen.getByTestId('submission-url-input').fill('https://example.test/raw');
	await screen.getByTestId('submission-convert-step-markitdown').click();
	await screen.getByTestId('submission-submit-button').click();

	await expect.element(screen.getByTestId('submission-accepted')).toBeVisible();
	expect(api.submitUrl).toHaveBeenCalledWith('https://example.test/raw', {
		userPrompt: undefined,
		convertSteps: { markitdown: false }
	});
});
