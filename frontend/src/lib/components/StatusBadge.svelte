<script lang="ts">
	import type { LifecycleStage } from '$lib/types';

	interface Props {
		status: LifecycleStage;
	}

	let { status }: Props = $props();

	const labels: Record<LifecycleStage, string> = {
		received: 'Received',
		converting: 'Converting',
		queued: 'Queued',
		running: 'Running',
		completed: 'Completed',
		failed: 'Failed'
	};

	const colorClasses: Record<LifecycleStage, string> = {
		received: 'bg-stage-received/10 text-stage-received',
		converting: 'bg-stage-converting/10 text-stage-converting',
		queued: 'bg-stage-queued/10 text-stage-queued',
		running: 'bg-stage-running/10 text-stage-running',
		completed: 'bg-stage-completed/10 text-stage-completed',
		failed: 'bg-stage-failed/10 text-stage-failed'
	};
</script>

<span
	class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium {colorClasses[
		status
	]}"
	data-testid="status-badge"
	data-status={status}
>
	{labels[status]}
</span>
