<script lang="ts">
	// The model choice offered in the three places the design puts it: the Ingest dialog, the
	// Run Lint popover, and quietly under the Ask composer ("for ask the model selection
	// should be inside the view. for ask a bit less prominent", chat 3).
	//
	// TODO(backend): no submission contract carries a model today — POST /api/ingest-submissions
	// takes a prompt and convert steps, POST /api/lint-runs takes nothing, and a query turn takes
	// the prompt alone. The choice is remembered per surface in the browser and is NOT sent, so
	// this control is deliberately inert until the Hub accepts it.
	interface Props {
		models: readonly string[];
		selected: string;
		onSelect: (model: string) => void;
		/** `options` is the labelled block form; `pills` is the quiet inline row. */
		variant?: 'options' | 'pills';
		/** `options` blocks stack in the narrow Run Lint popover and sit in a row in the dialog. */
		direction?: 'row' | 'column';
		notes?: Record<string, string>;
		testId?: string;
	}

	let {
		models,
		selected,
		onSelect,
		variant = 'options',
		direction = 'row',
		notes = {},
		testId
	}: Props = $props();
</script>

<div
	class="flex flex-wrap gap-2"
	class:flex-col={direction === 'column'}
	data-testid={testId ?? 'model-picker'}
	role="group"
	aria-label="Model"
>
	{#each models as model (model)}
		<button
			type="button"
			class={variant === 'pills'
				? 'rounded-full border px-3 py-0.5 text-xs ' +
					(model === selected
						? 'border-blue-500 bg-blue-50 text-blue-700'
						: 'border-slate-300 text-slate-500 hover:border-slate-400 hover:text-slate-700')
				: 'flex flex-col items-start rounded border px-3 py-1.5 text-left ' +
					(model === selected
						? 'border-blue-500 bg-blue-50'
						: 'border-slate-300 hover:border-slate-400')}
			aria-pressed={model === selected}
			onclick={() => onSelect(model)}
			data-testid="model-option"
			data-model={model}
		>
			<span class={variant === 'pills' ? '' : 'text-sm text-slate-900'}>{model}</span>
			{#if variant === 'options' && notes[model]}
				<span class="text-xs text-slate-400">{notes[model]}</span>
			{/if}
		</button>
	{/each}
</div>
