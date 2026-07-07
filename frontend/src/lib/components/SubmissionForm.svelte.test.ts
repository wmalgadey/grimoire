import { render } from 'vitest-browser-svelte';
import { expect, test, vi } from 'vitest';
import SubmissionForm from './SubmissionForm.svelte';

// T023 (US1): submitting via SubmissionForm shows an immediate acceptance message and a
// non-terminal task state, for both file and URL submissions (Acceptance Scenarios 1-2).

vi.mock('$lib/services/ingestSubmissionsApi', async () => {
	const actual = await vi.importActual<typeof import('$lib/services/ingestSubmissionsApi')>(
		'$lib/services/ingestSubmissionsApi'
	);
	return {
		...actual,
		submitUrl: vi.fn(),
		submitFile: vi.fn()
	};
});

test('URL submission shows immediate acceptance with a non-terminal status', async () => {
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitUrl).mockResolvedValue({
		taskId: 'task-url-1',
		status: 'received',
		sourceKind: 'url',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(SubmissionForm);

	await screen.getByTestId('submission-url-input').fill('https://example.test/article');
	await screen.getByTestId('submission-submit-button').click();

	await expect.element(screen.getByTestId('submission-accepted')).toBeVisible();
	await expect.element(screen.getByTestId('submission-accepted')).toHaveTextContent('task-url-1');
	await expect.element(screen.getByTestId('status-badge')).toHaveTextContent('Received');
	expect(api.submitUrl).toHaveBeenCalledWith('https://example.test/article');
});

test('File submission shows immediate acceptance with a non-terminal status', async () => {
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitFile).mockResolvedValue({
		taskId: 'task-file-1',
		status: 'received',
		sourceKind: 'markdown_file',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(SubmissionForm);

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
	const api = await import('$lib/services/ingestSubmissionsApi');
	vi.mocked(api.submitUrl).mockClear();

	const screen = await render(SubmissionForm);
	await screen.getByTestId('submission-submit-button').click();

	await expect.element(screen.getByTestId('submission-error')).toBeVisible();
	expect(api.submitUrl).not.toHaveBeenCalled();
});
