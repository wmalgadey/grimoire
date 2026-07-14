<script lang="ts">
	import type { ConnectionState } from '$lib/types';

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

	const colorClasses: Record<ConnectionState, string> = {
		connecting: 'bg-amber-500/10 text-amber-600',
		connected: 'bg-emerald-500/10 text-emerald-600',
		reconnecting: 'bg-amber-500/10 text-amber-600',
		disconnected: 'bg-red-500/10 text-red-600'
	};
</script>

<span
	class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium {colorClasses[
		state
	]}"
	data-testid="connection-status-indicator"
	data-connection-state={state}
>
	<span class="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true"></span>
	{labels[state]}
</span>
