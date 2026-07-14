import { render } from 'vitest-browser-svelte';
import { expect, test } from 'vitest';
import ConnectionStatusIndicator from './ConnectionStatusIndicator.svelte';
import type { ConnectionState } from '$lib/types';

// T053 (004 US4, FR-023/SC-012): the board's connection-health indicator renders a
// distinct label/style per connection state, with a stable testid/state attribute the
// board route and future tests can key off.

const cases: Array<{ state: ConnectionState; label: string }> = [
	{ state: 'connecting', label: 'Connecting' },
	{ state: 'connected', label: 'Connected' },
	{ state: 'reconnecting', label: 'Reconnecting' },
	{ state: 'disconnected', label: 'Disconnected' }
];

for (const { state, label } of cases) {
	test(`renders the ${state} state with its label and state attribute`, async () => {
		const screen = await render(ConnectionStatusIndicator, { state });

		const indicator = screen.getByTestId('connection-status-indicator');
		await expect.element(indicator).toHaveTextContent(label);
		await expect.element(indicator).toHaveAttribute('data-connection-state', state);
	});
}

test('connected and disconnected states render with distinct styling', async () => {
	const connectedScreen = await render(ConnectionStatusIndicator, { state: 'connected' });
	const connectedClass = connectedScreen
		.getByTestId('connection-status-indicator')
		.element().className;
	connectedScreen.unmount();

	const disconnectedScreen = await render(ConnectionStatusIndicator, { state: 'disconnected' });
	const disconnectedClass = disconnectedScreen
		.getByTestId('connection-status-indicator')
		.element().className;
	disconnectedScreen.unmount();

	expect(connectedClass).not.toBe(disconnectedClass);
});
