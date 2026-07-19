<script lang="ts">
	import { onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import TaskRecordView from '$lib/components/TaskRecordView.svelte';
	import { getTaskRecord } from '$lib/services/ingestSubmissionsApi';
	import type { TaskRecord } from '$lib/types';
	import type { PageProps } from './$types';

	let { data }: PageProps = $props();

	let record: TaskRecord | null = $state(null);
	let loaded = $state(false);

	async function refresh() {
		const result = await getTaskRecord(data.taskId);
		record = result.status === 'ok' ? result.record : null;
		loaded = true;
	}

	onMount(() => {
		void refresh();
	});
</script>

<svelte:head>
	<title>Task {data.taskId} — Grimoire</title>
</svelte:head>

<main class="mx-auto flex min-h-screen max-w-3xl flex-col gap-4 bg-white p-6">
	<header class="flex items-center justify-between gap-2">
		<h1 class="truncate text-lg font-semibold text-slate-900" data-testid="task-record-page-title">
			Task {data.taskId}
		</h1>
		<a href={resolve('/')} class="shrink-0 text-sm text-slate-500 underline hover:no-underline"
			>Back to board</a
		>
	</header>

	{#if loaded}
		<TaskRecordView {record} />
	{/if}
</main>
