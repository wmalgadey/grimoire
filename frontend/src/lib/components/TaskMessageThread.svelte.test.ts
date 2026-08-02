import { render } from 'vitest-browser-svelte';
import { beforeEach, expect, test, vi } from 'vitest';
import TaskMessageThread from './TaskMessageThread.svelte';
import type { RemediationTaskAttachedContext, RemediationTaskMessage } from '$lib/types';

// T043 (015-lint-board-parity, US5): attached-context/message thread rendering (FR-014,
// visible in every state) plus the attach-context/send-message compose forms (FR-011/
// FR-012, visible only while `proposed`).

const { attachMock, sendMock } = vi.hoisted(() => ({
	attachMock: vi.fn(),
	sendMock: vi.fn()
}));

vi.mock('$lib/services/remediationApi', async (importOriginal) => {
	const actual = await importOriginal<typeof import('$lib/services/remediationApi')>();
	return {
		...actual,
		attachRemediationTaskContext: attachMock,
		sendRemediationTaskMessage: sendMock
	};
});

const context: RemediationTaskAttachedContext[] = [
	{ content: 'Use the tag taxonomy from index.md.', attachedAt: new Date().toISOString() }
];

beforeEach(() => {
	attachMock.mockReset();
	sendMock.mockReset();
});

const messages: RemediationTaskMessage[] = [
	{ sender: 'human', content: 'Why this tag?', timestamp: new Date().toISOString() },
	{
		sender: 'agent',
		content: 'Because **it documents configuration**.',
		timestamp: new Date().toISOString()
	}
];

test('proposed task: renders attached context and messages, and shows both compose forms', async () => {
	const screen = await render(TaskMessageThread, {
		taskId: '2026-08-01-remediation-abc123',
		taskState: 'proposed',
		attachedContext: context,
		messages,
		messageTurnActive: false
	});

	await expect
		.element(screen.getByTestId('task-message-thread-context-item'))
		.toHaveTextContent('Use the tag taxonomy from index.md.');

	const rendered = screen.getByTestId('task-message-thread-message').elements();
	expect(rendered).toHaveLength(2);
	await expect.element(rendered[0]).toHaveAttribute('data-sender', 'human');
	await expect.element(rendered[1]).toHaveAttribute('data-sender', 'agent');
	// Agent content is markdown-rendered (bold survives as an element, not literal `**`).
	await expect.element(rendered[1]).toHaveTextContent('Because it documents configuration.');

	await expect.element(screen.getByTestId('task-message-thread-context-form')).toBeInTheDocument();
	await expect.element(screen.getByTestId('task-message-thread-send-form')).toBeInTheDocument();
});

test('non-proposed task: attached context and messages remain visible, but compose forms are hidden (FR-014)', async () => {
	const screen = await render(TaskMessageThread, {
		taskId: '2026-08-01-remediation-abc123',
		taskState: 'completed',
		attachedContext: context,
		messages,
		messageTurnActive: false
	});

	await expect
		.element(screen.getByTestId('task-message-thread-context-item'))
		.toHaveTextContent('Use the tag taxonomy from index.md.');
	expect(screen.getByTestId('task-message-thread-message').elements()).toHaveLength(2);

	await expect
		.element(screen.getByTestId('task-message-thread-context-form'))
		.not.toBeInTheDocument();
	await expect.element(screen.getByTestId('task-message-thread-send-form')).not.toBeInTheDocument();
});

test('sending a message calls the API with the task id and trimmed content, then clears the input', async () => {
	sendMock.mockResolvedValueOnce({
		taskId: '2026-08-01-remediation-abc123',
		messageTurnId: '2026-08-01-remtask-msg-def456',
		state: 'running',
		acceptedAt: new Date().toISOString()
	});

	const screen = await render(TaskMessageThread, {
		taskId: '2026-08-01-remediation-abc123',
		taskState: 'proposed',
		attachedContext: [],
		messages: [],
		messageTurnActive: false
	});

	const input = screen.getByTestId('task-message-thread-send-input');
	await input.fill('  Why does this need the configuration tag?  ');
	await screen.getByTestId('task-message-thread-send-button').click();

	expect(sendMock).toHaveBeenCalledExactlyOnceWith(
		'2026-08-01-remediation-abc123',
		'Why does this need the configuration tag?'
	);
	await expect.element(input).toHaveValue('');
	await expect
		.element(screen.getByTestId('task-message-thread-send-error'))
		.not.toBeInTheDocument();
});

test('attaching context calls the API with the task id and trimmed content', async () => {
	attachMock.mockResolvedValueOnce({
		taskId: '2026-08-01-remediation-abc123',
		attachedAt: new Date().toISOString()
	});

	const screen = await render(TaskMessageThread, {
		taskId: '2026-08-01-remediation-abc123',
		taskState: 'proposed',
		attachedContext: [],
		messages: [],
		messageTurnActive: false
	});

	const input = screen.getByTestId('task-message-thread-context-input');
	await input.fill('  Prefer the existing taxonomy.  ');
	await screen.getByTestId('task-message-thread-context-submit').click();

	expect(attachMock).toHaveBeenCalledExactlyOnceWith(
		'2026-08-01-remediation-abc123',
		'Prefer the existing taxonomy.'
	);
	await expect.element(input).toHaveValue('');
});

test('an active message turn disables the send box and shows a responding hint', async () => {
	const screen = await render(TaskMessageThread, {
		taskId: '2026-08-01-remediation-abc123',
		taskState: 'proposed',
		attachedContext: [],
		messages: [],
		messageTurnActive: true
	});

	await expect.element(screen.getByTestId('task-message-thread-turn-active')).toBeInTheDocument();
	await expect.element(screen.getByTestId('task-message-thread-send-input')).toBeDisabled();
	await expect.element(screen.getByTestId('task-message-thread-send-button')).toBeDisabled();
});

test('empty message submission shows a client-side error and never calls the API', async () => {
	const screen = await render(TaskMessageThread, {
		taskId: '2026-08-01-remediation-abc123',
		taskState: 'proposed',
		attachedContext: [],
		messages: [],
		messageTurnActive: false
	});

	await screen.getByTestId('task-message-thread-send-button').click();

	await expect.element(screen.getByTestId('task-message-thread-send-error')).toBeInTheDocument();
	expect(sendMock).not.toHaveBeenCalled();
});
