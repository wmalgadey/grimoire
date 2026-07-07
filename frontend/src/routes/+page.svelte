<script lang="ts">
	import SubmissionForm from '$lib/components/SubmissionForm.svelte';
</script>

<svelte:head>
	<title>Submit a source — Grimoire</title>
</svelte:head>

<main class="mx-auto flex max-w-xl flex-col gap-6 p-6">
	<header class="flex flex-col gap-1">
		<h1 class="text-lg font-semibold text-slate-900 dark:text-slate-100">Submit a source</h1>
		<p class="text-sm text-slate-500 dark:text-slate-400">
			Submit a URL, Markdown, PDF, or Office document to ingest into the wiki.
		</p>
	</header>

	<SubmissionForm />
</main>
