<script lang="ts">
	// #149: the three surfaces that used to offer a model picker now state the model instead.
	// One component so the wording cannot drift between them — the Ingest dialog, the Run Lint
	// popover and the Ask composer are meant to say the same thing, because they run the same
	// model. See $lib/models.ts for why this is a constant and what would replace it (#84).
	import { ACTIVE_MODEL } from '$lib/models';

	interface Props {
		/** Per-surface, so a test can name the surface it is asserting on. */
		testId?: string;
	}

	let { testId }: Props = $props();
</script>

<p class="text-xs text-slate-500" data-testid={testId ?? 'active-model'}>
	Model: <span class="font-mono text-slate-700">{ACTIVE_MODEL}</span>
</p>
