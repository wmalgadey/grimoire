<script lang="ts">
	import type { ConnectionState } from '$lib/types';

	// 004 FR-023/SC-012 unchanged: a persistent, always-mounted projection of the board's own
	// SignalR connection lifecycle, with a distinct style and label per state.
	//
	// The design reduces it to a dot in the nav — "could be just a symbol and a small hover
	// display with server details" (chat 3) — so the label and its explanation move into a
	// panel revealed on hover or keyboard focus. The label stays in the accessible name and in
	// the panel; it is never the only thing carrying the state, which is also on the dot's
	// colour and on `data-connection-state`.
	interface Props {
		state: ConnectionState;
	}

	let { state }: Props = $props();

	const labels: Record<ConnectionState, string> = {
		connecting: 'Connecting',
		connected: 'Connected',
		reconnecting: 'Reconnecting…',
		disconnected: 'Disconnected'
	};

	const descriptions: Record<ConnectionState, string> = {
		connecting: 'Opening the live connection. The board fills in as soon as it is up.',
		connected: 'Live task updates are streaming. The board reflects the server as it changes.',
		reconnecting:
			'The live connection dropped and is being retried. What you see may be a moment behind.',
		disconnected: 'No live connection, so the board is not updating until it comes back.'
	};

	const colorClasses: Record<ConnectionState, string> = {
		connecting: 'text-amber-600',
		connected: 'text-emerald-600',
		reconnecting: 'text-amber-600',
		disconnected: 'text-red-600'
	};

	// The panel is revealed by hover for a pointer and by focus for a keyboard or a tap —
	// which is why the dot is a real button: focusable, and never a hover-only affordance.
	// (Note for future edits: a variable named `state` is in scope here, so `$state(...)` in
	// this file parses as a store subscription of that prop, not as the rune. Keep this
	// component rune-free unless the prop is renamed.)
</script>

<button
	type="button"
	class="group relative inline-grid h-7 w-7 place-items-center rounded-full hover:bg-slate-100 {colorClasses[
		state
	]}"
	aria-label="Connection: {labels[state]}"
	data-testid="connection-status-indicator"
	data-connection-state={state}
>
	<span
		class="h-2.5 w-2.5 rounded-full bg-current"
		class:animate-pulse={state === 'connected' || state === 'reconnecting'}
		aria-hidden="true"
	></span>
	<span
		class="pointer-events-none absolute top-9 right-0 z-40 hidden w-64 flex-col gap-1 rounded-lg border border-slate-200 bg-white p-3 text-left shadow-lg group-hover:flex group-focus:flex"
		data-testid="connection-status-detail"
	>
		<span class="text-sm font-medium text-slate-900">{labels[state]}</span>
		<span class="text-xs text-slate-500">{descriptions[state]}</span>
		<span class="text-xs text-slate-400">
			Drops in the connection show here, and the board reconnects on its own.
		</span>
	</span>
</button>
